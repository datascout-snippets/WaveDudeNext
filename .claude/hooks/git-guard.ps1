# Claude Code hooks (2026-10-16): the agent never commits on its own, and the user never jumps through a hoop.
# In PowerShell since 2026-11-14 (the user's rule: no Python in this project, nowhere).
#
#   record  (UserPromptSubmit)  stores the user's latest message for this session.
#   guard   (PreToolUse, Bash and PowerShell)  lets a git command that changes the repository (commit, push,
#           reset, rebase, merge, tag, revert, cherry-pick, restore, clean, stash, checkout, am) run only when
#           the user's latest message asks for that very action by name ("commit", "push", ...), not negated
#           ("don't commit"). Anything else is denied, in every permission mode. Reads (status, diff, log) pass.
#
# The user's word is the permission, exactly as the rule was given: "commit" said for the change at hand.
#
# The hook's input is JSON on the standard input, UTF-8, read as bytes: the console's own code page would garble a
# message's umlauts. A denial is JSON on the standard output; no output lets the command run.
param([string]$Mode = 'guard')

$verbs = 'commit|push|reset|rebase|merge|tag|revert|cherry-pick|restore|clean|stash|checkout|am'
# the command's words are matched as written, the message's without regard to case
$guarded = [regex]::new('\bgit\b[^|;&\n]*\b(' + $verbs + ')\b')
$negated = [regex]::new('\b(don''?t|do not|not|never|no|without|stop|instead of)\b(\W+\w+){0,2}\W+\b(' + $verbs + ')',
	[Text.RegularExpressions.RegexOptions]::IgnoreCase)

$bytes = New-Object IO.MemoryStream
[Console]::OpenStandardInput().CopyTo($bytes)
$data = $null
try { $data = [Text.Encoding]::UTF8.GetString($bytes.ToArray()) | ConvertFrom-Json } catch { }

$session = [string]$data.session_id
if ($session -eq '') { $session = 'none' }
$promptFile = Join-Path ([IO.Path]::GetTempPath()) ('claude-last-prompt-' + [regex]::Replace($session, '[^\w-]', '_') + '.txt')
$utf8 = New-Object Text.UTF8Encoding $false

if ($Mode -eq 'record') {
	[IO.File]::WriteAllText($promptFile, [string]$data.prompt, $utf8)
	exit 0
}

$match = $guarded.Match([string]$data.tool_input.command)
if (-not $match.Success) { exit 0 }
$verb = $match.Groups[1].Value
$prompt = ''
try { $prompt = [IO.File]::ReadAllText($promptFile, $utf8) } catch { }
$asked = [regex]::IsMatch($prompt, '\b' + [regex]::Escape($verb) + '\w*', [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
	-not $negated.IsMatch($prompt)
if ($asked) { exit 0 }  # the user asked for it in their latest message: the command runs

$reason = "Blocked: 'git $verb' changes the repository, and the user's latest message does not ask for it by name ($verb). " +
	'The agent must not do this on its own; it runs only after the user asks for it.'
[Console]::Out.Write((@{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; permissionDecision = 'deny'; permissionDecisionReason = $reason } } |
	ConvertTo-Json -Compress -Depth 4))
exit 0
