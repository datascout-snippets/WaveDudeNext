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

using System.Text;
using System.Text.Json;

namespace WaveDudeNext.UI.Docking;

/// <summary>
/// A <see cref="DockLayout"/> as JSON, for a head that keeps its settings where JSON is the usual form (the browser
/// head's local storage, 2026-11-14, the user's choice; the desktop heads' settings file holds the layout as XML).
/// The layout is a tree of any depth, so it is one value: its root node, each node an object named by its
/// <c>kind</c>:
/// <code>
/// {"kind":"split","orientation":"Horizontal","length":1,"children":[
///   {"kind":"split","orientation":"Vertical","length":343,"children":[
///     {"kind":"group","items":["project"],"active":"project","length":253},
///     {"kind":"group","items":["properties"],"active":"properties","length":506}]},
///   {"kind":"document","length":1029}]}
/// </code>
/// Written and read by hand, without reflection, which a trimmed program does not have for JSON. Thread-free.
/// </summary>
public static class DockLayoutJson
{
	private const string Kind = "kind";
	private const string Split = "split";
	private const string Group = "group";
	private const string Document = "document";
	private const string Orientation = "orientation";
	private const string Length = "length";
	private const string Children = "children";
	private const string Items = "items";
	private const string Active = "active";

	/// <summary>The layout's tree as one line of JSON; <c>null</c> for a layout without a root.</summary>
	public static string Write(DockLayout layout)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			WriteNode(writer, layout.Root);
		}
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	/// <summary>The layout a text written by <see cref="Write"/> holds.</summary>
	/// <exception cref="JsonException">The text is not JSON, or not a layout's: a node of an unknown kind, a member of the wrong form.</exception>
	public static DockLayout Read(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		try
		{
			return new DockLayout { Root = ReadNode(document.RootElement) };
		}
		catch (InvalidOperationException ex)
		{
			// a member of another JSON kind than its reader asked for (a number where a name belongs)
			throw new JsonException("A member of the layout is of the wrong form.", ex);
		}
	}

	private static void WriteNode(Utf8JsonWriter writer, NodeLayout? node)
	{
		if (node == null)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStartObject();
		switch (node)
		{
			case SplitLayout split:
				writer.WriteString(Kind, Split);
				writer.WriteString(Orientation, split.Orientation.ToString());
				writer.WriteNumber(Length, split.Length);
				writer.WriteStartArray(Children);
				foreach (NodeLayout child in split.Children)
				{
					WriteNode(writer, child);
				}
				writer.WriteEndArray();
				break;
			case GroupLayout group:
				writer.WriteString(Kind, Group);
				writer.WriteStartArray(Items);
				foreach (string item in group.Items)
				{
					writer.WriteStringValue(item);
				}
				writer.WriteEndArray();
				if (group.Active != null)
				{
					writer.WriteString(Active, group.Active);
				}
				writer.WriteNumber(Length, group.Length);
				break;
			default:
				writer.WriteString(Kind, Document);
				writer.WriteNumber(Length, node.Length);
				break;
		}
		writer.WriteEndObject();
	}

	private static NodeLayout? ReadNode(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Null)
		{
			return null;
		}
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(Kind, out JsonElement kind) || kind.ValueKind != JsonValueKind.String)
		{
			throw new JsonException("A node of the layout is not an object with a kind.");
		}
		NodeLayout node;
		switch (kind.GetString())
		{
			case Split:
				var split = new SplitLayout();
				if (element.TryGetProperty(Orientation, out JsonElement orientation))
				{
					split.Orientation = Enum.TryParse(orientation.GetString(), out LayoutDirection direction) && Enum.IsDefined(direction)
						? direction
						: throw new JsonException($"A split's orientation is '{orientation}', neither Horizontal nor Vertical.");
				}
				if (element.TryGetProperty(Children, out JsonElement children))
				{
					foreach (JsonElement child in ListOf(children, Children))
					{
						if (ReadNode(child) is { } childNode)
						{
							split.Children.Add(childNode);
						}
					}
				}
				node = split;
				break;
			case Group:
				var group = new GroupLayout();
				if (element.TryGetProperty(Items, out JsonElement items))
				{
					foreach (JsonElement item in ListOf(items, Items))
					{
						group.Items.Add(item.GetString() ?? throw new JsonException("An item of a group is not an id."));
					}
				}
				if (element.TryGetProperty(Active, out JsonElement active))
				{
					group.Active = active.GetString();
				}
				node = group;
				break;
			case Document:
				node = new DocumentLayout();
				break;
			default:
				throw new JsonException($"A node of the layout is of the unknown kind '{kind}'.");
		}
		if (element.TryGetProperty(Length, out JsonElement length))
		{
			node.Length = length.ValueKind == JsonValueKind.Number && length.TryGetDouble(out double value) && value > 0 && double.IsFinite(value)
				? value
				: throw new JsonException($"A node's length is '{length}', not a positive number.");
		}
		return node;
	}

	private static JsonElement.ArrayEnumerator ListOf(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : throw new JsonException($"A node's {name} are not a list.");
}
