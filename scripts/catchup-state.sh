#!/usr/bin/env bash
# This repo's own reader for /catchup (.claude/skills/catchup/catchup.conf runs it). It prints the
# shared reader's typed lines (format: the header of .claude/skills/catchup/catchup-state.sh) for
# the forge state the shared reader leaves to each repo:
#   ci-failing        the newest finished run of a workflow on main failed       route /ci-triage
#   server-abi-issue  the weekly watch's failure issue is open                   route /issue-triage
#   pr-stuck          an open PR has a failing check or a merge conflict         route /ci-triage
#   open-prs          INFO: how many PRs are open
# Read-only: it only calls gh. A gh call that fails prints an ERROR line for its section and the
# other sections still run.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 2

# The watch finds its issue by exact title, so read the title from the workflow, not a copy.
watch=.github/workflows/server-abi-watch.yml
issue_title="$(sed -n 's/^ *ISSUE_TITLE: *//p' "$watch" | head -1)"

if runs="$(gh run list --branch main --limit 50 --json databaseId,workflowName,status,conclusion,url 2>&1)"; then
  jq -r '[.[] | select(.status == "completed")] | group_by(.workflowName) | map(max_by(.databaseId))
         | .[] | select(.conclusion == "failure")
         | "BANNER\tci-failing\t\(.workflowName) failed on main\t/ci-triage \(.databaseId)\n  \(.url)"' <<<"$runs"
else
  printf 'ERROR\tci-failing\tgh run list failed: %s\n' "${runs//$'\n'/ }"
fi

if [ -z "$issue_title" ]; then
  printf 'ERROR\tserver-abi-issue\tno ISSUE_TITLE in %s\n' "$watch"
elif issues="$(gh issue list --state open --search "in:title \"$issue_title\"" --json number,title,url 2>&1)"; then
  jq -r --arg t "$issue_title" '.[] | select(.title == $t)
         | "BANNER\tserver-abi-issue\t#\(.number) \(.title)\t/issue-triage \(.number)\n  \(.url)"' <<<"$issues"
else
  printf 'ERROR\tserver-abi-issue\tgh issue list failed: %s\n' "${issues//$'\n'/ }"
fi

if prs="$(gh pr list --state open --json number,title,mergeable,statusCheckRollup,url 2>&1)"; then
  jq -r '.[] | ([.statusCheckRollup[]? | select((.conclusion // .state) | IN("FAILURE", "ERROR", "TIMED_OUT", "CANCELLED", "ACTION_REQUIRED"))
                 | (.name // .context)]) as $failed
         | select(.mergeable == "CONFLICTING" or ($failed | length) > 0)
         | "BANNER\tpr-stuck\t#\(.number) \(.title): "
           + (if .mergeable == "CONFLICTING" then "merge conflict" else "failing \($failed | join(", "))" end)
           + "\t/ci-triage\n  \(.url)"' <<<"$prs"
  printf 'INFO\topen-prs\t%s open PR(s)\n' "$(jq length <<<"$prs")"
else
  printf 'ERROR\tpr-stuck\tgh pr list failed: %s\n' "${prs//$'\n'/ }"
fi
