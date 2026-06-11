#!/bin/bash
# sprint-update.sh — Zero-token-cost SPRINT.md table updater.
# Mechanically updates table cells so the LLM doesn't burn tokens on file I/O.
#
# Usage:
#   bash .claude/scripts/sprint-update.sh status <issue-id> <value>
#   bash .claude/scripts/sprint-update.sh postmerge <issue-id> <value>
#   bash .claude/scripts/sprint-update.sh sprint-status <value>
#
# Examples:
#   bash .claude/scripts/sprint-update.sh status S1-001 in-progress
#   bash .claude/scripts/sprint-update.sh postmerge S1-002 pass
#   bash .claude/scripts/sprint-update.sh sprint-status in-progress

SPRINT_FILE="SPRINT.md"
METRICS_SCRIPT="$(dirname "$0")/sprint-metrics.sh"

die() { echo "Error: $1" >&2; exit 1; }

# Best-effort sprint-metrics emission (observability only). Guarded by jq
# availability and suffixed `|| true` — a metrics failure must NEVER fail the
# SPRINT.md update (the update path is sprint-critical, metrics are not).
emit_metrics() {
  if ! command -v jq >/dev/null 2>&1; then
    echo "note: jq not found — sprint metrics not recorded" >&2
    return 0
  fi
  [ -f "$METRICS_SCRIPT" ] || return 0
  bash "$METRICS_SCRIPT" "$@" >/dev/null 2>&1 || true
}

if [ $# -lt 1 ]; then
  die "Usage: sprint-update.sh <status|postmerge|sprint-status|phase> [issue-id] <value>"
fi

subcmd="$1"
shift

[ -f "$SPRINT_FILE" ] || die "SPRINT.md not found in current directory"

case "$subcmd" in
  status)
    [ $# -eq 2 ] || die "Usage: sprint-update.sh status <issue-id> <value>"
    issue_id="$1"
    new_value="$2"

    # Column 5 (Status) in the pipe-delimited table
    # Find the row starting with | <issue-id> | and replace column 5
    if ! grep -q "^| *${issue_id} *|" "$SPRINT_FILE"; then
      die "Issue '${issue_id}' not found in SPRINT.md"
    fi

    awk -v id="$issue_id" -v val="$new_value" '
    BEGIN { FS="|"; OFS="|" }
    {
      # Match table rows where field 2 (trimmed) equals the issue ID
      trimmed = $2
      gsub(/^ +| +$/, "", trimmed)
      if (trimmed == id) {
        # Replace field 6 (Status column, 1-indexed with leading empty field)
        $6 = " " val " "
      }
      print
    }
    ' "$SPRINT_FILE" > "${SPRINT_FILE}.tmp" && mv "${SPRINT_FILE}.tmp" "$SPRINT_FILE"

    emit_metrics status "$issue_id" "$new_value"
    ;;

  postmerge)
    [ $# -ge 2 ] || die "Usage: sprint-update.sh postmerge <issue-id> <value>"
    issue_id="$1"
    shift
    # Join remaining args to support "pending: some description"
    new_value="$*"

    if ! grep -q "^| *${issue_id} *|" "$SPRINT_FILE"; then
      die "Issue '${issue_id}' not found in SPRINT.md"
    fi

    awk -v id="$issue_id" -v val="$new_value" '
    BEGIN { FS="|"; OFS="|" }
    {
      trimmed = $2
      gsub(/^ +| +$/, "", trimmed)
      if (trimmed == id) {
        # Replace field 7 (Post-Merge column) — last data field before trailing |
        $7 = " " val " "
      }
      print
    }
    ' "$SPRINT_FILE" > "${SPRINT_FILE}.tmp" && mv "${SPRINT_FILE}.tmp" "$SPRINT_FILE"
    ;;

  sprint-status)
    [ $# -eq 1 ] || die "Usage: sprint-update.sh sprint-status <value>"
    new_value="$1"

    if ! grep -q '^\*\*Status:\*\*' "$SPRINT_FILE"; then
      die "Sprint status line not found in SPRINT.md"
    fi

    awk -v val="$new_value" '
    /^\*\*Status:\*\*/ { print "**Status:** " val; next }
    { print }
    ' "$SPRINT_FILE" > "${SPRINT_FILE}.tmp" && mv "${SPRINT_FILE}.tmp" "$SPRINT_FILE"

    # Recorded in the phases timeline (the metrics script has no sprint-level
    # status concept; prefixing keeps it distinguishable from phase names).
    emit_metrics phase "sprint-status:$new_value"
    ;;

  phase)
    [ $# -eq 1 ] || die "Usage: sprint-update.sh phase <value>"
    new_value="$1"

    if grep -q '^\*\*Phase:\*\*' "$SPRINT_FILE"; then
      awk -v val="$new_value" '
      /^\*\*Phase:\*\*/ { print "**Phase:** " val; next }
      { print }
      ' "$SPRINT_FILE" > "${SPRINT_FILE}.tmp" && mv "${SPRINT_FILE}.tmp" "$SPRINT_FILE"
    else
      # Insert Phase line after Status line (first occurrence)
      awk -v val="$new_value" '
      /^\*\*Status:\*\*/ && !done { print; print "**Phase:** " val; done=1; next }
      { print }
      ' "$SPRINT_FILE" > "${SPRINT_FILE}.tmp" && mv "${SPRINT_FILE}.tmp" "$SPRINT_FILE"
    fi

    emit_metrics phase "$new_value"
    ;;

  *)
    die "Unknown subcommand '${subcmd}'. Usage: sprint-update.sh <status|postmerge|sprint-status|phase> [issue-id] <value>"
    ;;
esac
