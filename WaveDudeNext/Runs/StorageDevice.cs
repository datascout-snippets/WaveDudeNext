// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WaveDudeNext.Runs;

/// <summary>
/// What kind of storage holds a path (2026-10-31, for the first start's choice of the preview quality): whether the
/// volume incurs a seek penalty, Windows' own distinction between a hard disk and a solid-state drive, the one its
/// drive optimiser goes by. Asked with <c>IOCTL_STORAGE_QUERY_PROPERTY</c> on handles opened without access rights, so
/// no elevation is needed: of the volume first, which answers for the disk beneath a simple volume, then of each disk
/// the volume lies on. A RAID controller's virtual disk answers neither way (the workstation's C: on a PERC H310, whose
/// media type Windows itself calls unspecified, while its member SSDs answer), so the kind can stay unknown. Thread-free.
/// </summary>
public static class StorageDevice
{
	private const uint FileShareRead = 0x1U;
	private const uint FileShareWrite = 0x2U;
	private const uint OpenExisting = 3U;

	/// <summary>CTL_CODE(IOCTL_STORAGE_BASE, 0x500, METHOD_BUFFERED, FILE_ANY_ACCESS).</summary>
	private const uint IoctlStorageQueryProperty = 0x002D1400U;

	/// <summary>CTL_CODE(IOCTL_VOLUME_BASE, 0, METHOD_BUFFERED, FILE_ANY_ACCESS): the disks a volume lies on.</summary>
	private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000U;

	/// <summary>STORAGE_PROPERTY_ID.StorageDeviceSeekPenaltyProperty.</summary>
	private const uint StorageDeviceSeekPenaltyProperty = 7U;

	/// <summary>STORAGE_QUERY_TYPE.PropertyStandardQuery.</summary>
	private const uint PropertyStandardQuery = 0U;

	/// <summary>Disks a volume may span before the question is given up; <c>VOLUME_DISK_EXTENTS</c> is a count, padding and 24 bytes per extent.</summary>
	private const int MaxExtents = 16;

	/// <summary>
	/// Whether the volume holding <paramref name="path"/> incurs a seek penalty: true for a hard disk, or a volume with
	/// one among its disks; false for solid-state drives; null when the question cannot be answered (a relative or UNC
	/// path, a volume and disks that do not report the property on Windows; on Linux a mount whose block device has
	/// no queue in sysfs, a network or a multi-device file system; macOS, where the answer would need IOKit and the
	/// machines have solid-state drives). The path need not exist; on Windows only its drive letter is used, on Linux
	/// the mount it lies on (<c>/proc/self/mountinfo</c>, the device's <c>queue/rotational</c> under
	/// <c>/sys/dev/block</c>, a partition's at its disk; 2026-11-12, the Linux port).
	/// </summary>
	public static bool? IncursSeekPenalty(string path)
	{
		if (OperatingSystem.IsLinux())
		{
			return LinuxSeekPenalty(path);
		}
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}
		string? root = Path.GetPathRoot(path);
		// "C:\" and nothing else: a share or a device path has no volume to ask
		if (root is not { Length: 3 } || root[1] != ':' || root[2] != '\\')
		{
			return null;
		}
		using SafeFileHandle volume = Open(@"\\.\" + root[..2]);
		if (volume.IsInvalid)
		{
			return null;
		}
		if (QuerySeekPenalty(volume) is bool answer)
		{
			return answer;
		}
		// the volume's stack does not forward the question: ask the disks beneath it; a hard disk among them is a seek
		// penalty, every disk answering no is none, a disk without an answer leaves the kind unknown
		uint[] disks = DiskNumbers(volume);
		if (disks.Length == 0)
		{
			return null;
		}
		bool? result = false;
		foreach (uint disk in disks)
		{
			using SafeFileHandle device = Open(@"\\.\PhysicalDrive" + disk.ToString(CultureInfo.InvariantCulture));
			bool? penalty = device.IsInvalid ? null : QuerySeekPenalty(device);
			if (penalty == true)
			{
				return true;
			}
			if (penalty == null)
			{
				result = null;
			}
		}
		return result;
	}

	/// <summary>
	/// The volume holding <paramref name="path"/> for a log line: the drive letter on Windows ("C:"), the mount point on
	/// Linux ("/home"), else the path's root; the path itself where it has no root (2026-11-12, from <c>MachineFacts</c>).
	/// </summary>
	public static string VolumeOf(string path)
	{
		if (OperatingSystem.IsLinux())
		{
			try
			{
				if (MountOf(File.ReadAllText(MountInfoPath), Path.GetFullPath(path)) is { } mount)
				{
					return mount.MountPoint;
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// the root below serves
			}
		}
		string root = Path.GetPathRoot(path) ?? path;
		return OperatingSystem.IsWindows() ? root.TrimEnd('\\') : root;
	}

	private const string MountInfoPath = "/proc/self/mountinfo";

	private const string SysBlockPath = "/sys/dev/block";

	/// <summary>The mount holding a path on Linux: its mount point and its device's major:minor number, read from <c>/proc/self/mountinfo</c>.</summary>
	internal readonly record struct Mount(string MountPoint, string Device);

	/// <summary>
	/// The seek penalty of the block device beneath <paramref name="path"/>'s mount: the device's <c>queue/rotational</c> in
	/// sysfs (a disk's own, a partition's at its disk, a mapper device's own); null where the mount names no block device
	/// with a queue (a network or a multi-device file system, a kernel without the attribute).
	/// </summary>
	[SupportedOSPlatform("linux")]
	private static bool? LinuxSeekPenalty(string path)
	{
		try
		{
			if (MountOf(File.ReadAllText(MountInfoPath), Path.GetFullPath(path)) is not { } mount)
			{
				return null;
			}
			string? queue = QueueDirectory(Path.Combine(SysBlockPath, mount.Device));
			return queue == null ? null : ParseRotational(File.ReadAllText(Path.Combine(queue, "rotational")));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	/// <summary>The <c>queue</c> directory of a block device's sysfs entry (a symbolic link under <c>/sys/dev/block</c>): the entry's own, or its parent's for a partition; null without one.</summary>
	[SupportedOSPlatform("linux")]
	private static string? QueueDirectory(string entry)
	{
		if (!Directory.Exists(entry))
		{
			return null;
		}
		string device = Directory.ResolveLinkTarget(entry, returnFinalTarget: true)?.FullName ?? entry;
		string own = Path.Combine(device, "queue");
		if (Directory.Exists(own))
		{
			return own;
		}
		string? parent = Path.GetDirectoryName(device);
		return parent != null && Directory.Exists(Path.Combine(parent, "queue")) ? Path.Combine(parent, "queue") : null;
	}

	/// <summary>
	/// The mount holding <paramref name="fullPath"/> in the text of <c>/proc/self/mountinfo</c>: the line whose mount
	/// point (the fifth field, spaces and the like escaped as octal) is the longest that holds the path; null when none
	/// does. Exact for the harness.
	/// </summary>
	internal static Mount? MountOf(string mountInfo, string fullPath)
	{
		Mount? best = null;
		foreach (string line in mountInfo.Split('\n'))
		{
			string[] fields = line.Split(' ');
			if (fields.Length < 5)
			{
				continue;
			}
			string point = Unescape(fields[4]);
			bool holds = point == "/" ? fullPath.StartsWith('/') : fullPath == point || fullPath.StartsWith(point + "/", StringComparison.Ordinal);
			if (holds && (best == null || point.Length > best.Value.MountPoint.Length))
			{
				best = new Mount(point, fields[2]);
			}
		}
		return best;
	}

	/// <summary>A mountinfo field with its octal escapes resolved: <c>\040</c> a space, <c>\011</c> a tab, <c>\012</c> a newline, <c>\134</c> a backslash.</summary>
	private static string Unescape(string field)
	{
		if (!field.Contains('\\'))
		{
			return field;
		}
		var text = new System.Text.StringBuilder(field.Length);
		for (int i = 0; i < field.Length; i++)
		{
			if (field[i] == '\\' && i + 3 < field.Length && char.IsDigit(field[i + 1]) && char.IsDigit(field[i + 2]) && char.IsDigit(field[i + 3]))
			{
				text.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
				i += 3;
			}
			else
			{
				text.Append(field[i]);
			}
		}
		return text.ToString();
	}

	/// <summary>The text of a <c>queue/rotational</c> attribute: "1" a seek penalty, "0" none, anything else unknown. Exact for the harness.</summary>
	internal static bool? ParseRotational(string text) => text.Trim() switch { "1" => true, "0" => false, _ => null };

	/// <summary>Opens a volume or disk device for queries alone (no access right); the handle is invalid when it cannot be opened.</summary>
	private static SafeFileHandle Open(string device) => CreateFileW(device, 0U, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0U, IntPtr.Zero);

	/// <summary>The seek-penalty property of a volume or disk; null when its driver does not answer.</summary>
	private static bool? QuerySeekPenalty(SafeFileHandle device)
	{
		var query = new StoragePropertyQuery { PropertyId = StorageDeviceSeekPenaltyProperty, QueryType = PropertyStandardQuery };
		if (!DeviceIoControl(device, IoctlStorageQueryProperty, ref query, (uint)Marshal.SizeOf<StoragePropertyQuery>(),
			out DeviceSeekPenaltyDescriptor descriptor, (uint)Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(), out uint returned, IntPtr.Zero)
			|| returned < 9U)
		{
			return null;
		}
		return descriptor.IncursSeekPenalty != 0;
	}

	/// <summary>The numbers of the disks a volume lies on, each once; empty when the volume does not say or spans more than <see cref="MaxExtents"/>.</summary>
	private static uint[] DiskNumbers(SafeFileHandle volume)
	{
		byte[] buffer = new byte[8 + 24 * MaxExtents];
		if (!DeviceIoControl(volume, IoctlVolumeGetVolumeDiskExtents, IntPtr.Zero, 0U, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero) || returned < 8U)
		{
			return [];
		}
		int count = Math.Min((int)BitConverter.ToUInt32(buffer, 0), MaxExtents);
		var disks = new HashSet<uint>();
		for (int i = 0; i < count; i++)
		{
			disks.Add(BitConverter.ToUInt32(buffer, 8 + 24 * i));
		}
		return [.. disks];
	}

	/// <summary>The <c>STORAGE_PROPERTY_QUERY</c> structure: the property, the query type and one byte of parameters.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct StoragePropertyQuery
	{
		public uint PropertyId;
		public uint QueryType;
		public byte AdditionalParameters;
	}

	/// <summary>The <c>DEVICE_SEEK_PENALTY_DESCRIPTOR</c> structure: the header's version and size, then the BOOLEAN.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct DeviceSeekPenaltyDescriptor
	{
		public uint Version;
		public uint Size;
		public byte IncursSeekPenalty;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
	private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

	[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, ref StoragePropertyQuery inBuffer, uint inBufferSize,
		out DeviceSeekPenaltyDescriptor outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

	[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize,
		byte[] outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
}
