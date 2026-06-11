#!/bin/bash
# sprint-metrics.sh — Sprint metrics collection for retrospectives.
# Writes .sprint-metrics.json incrementally during a sprint.
#
# Usage:
#   bash .claude/scripts/sprint-metrics.sh init <sprint-id>
#   bash .claude/scripts/sprint-metrics.sh item-start <item-id>
#   bash .claude/scripts/sprint-metrics.sh item-complete <item-id>
#   bash .claude/scripts/sprint-metrics.sh cycle <item-id>
#   bash .claude/scripts/sprint-metrics.sh rework <item-id>
#   bash .claude/scripts/sprint-metrics.sh phase <phase-name>
#   bash .claude/scripts/sprint-metrics.sh status <item-id> <value>
#   bash .claude/scripts/sprint-metrics.sh finalize
#
# `phase` and `status` auto-initialize the metrics file when it is absent
# (sprint id parsed from SPRINT.md's "**Sprint:**" line, fallback "unknown")
# so best-effort callers (sprint-update.sh) never need a prior explicit init.

METRICS_FILE=".sprint-metrics.json"
SPRINT_FILE="SPRINT.md"
NOW=$(date -u +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -Iseconds)

die() { echo "Error: $1" >&2; exit 1; }

# Write a fresh metrics file (existing init shape + phases array), parsing the
# sprint id from SPRINT.md when available. Used by phase/status auto-init.
auto_init() {
  sprint_id="unknown"
  if [ -f "$SPRINT_FILE" ]; then
    parsed=$(grep -m1 '^\*\*Sprint:\*\*' "$SPRINT_FILE" | sed 's/^\*\*Sprint:\*\*[[:space:]]*//' | awk '{print $1}' | sed 's/[:,]$//')
    [ -n "$parsed" ] && sprint_id="$parsed"
  fi
  cat > "$METRICS_FILE" <<ENDJSON
{
  "sprintId": "${sprint_id}",
  "startedAt": "${NOW}",
  "completedAt": null,
  "phases": [],
  "items": [],
  "totals": {
    "planned": 0,
    "completed": 0,
    "rework": 0,
    "blocked": 0,
    "scopeChanges": 0,
    "contextCycles": 0
  }
}
ENDJSON
}

[ $# -ge 1 ] || die "Usage: sprint-metrics.sh <init|item-start|item-complete|cycle|rework|phase|status|finalize> [args]"

subcmd="$1"
shift

# All commands except init require jq and the metrics file.
# phase/status auto-init the file when absent (best-effort invocation path).
if [ "$subcmd" != "init" ]; then
  command -v jq >/dev/null 2>&1 || die "jq is required for sprint-metrics.sh"
  if [ ! -f "$METRICS_FILE" ]; then
    case "$subcmd" in
      phase|status) auto_init ;;
      *) die ".sprint-metrics.json not found — run 'sprint-metrics.sh init <sprint-id>' first" ;;
    esac
  fi
fi

case "$subcmd" in
  init)
    [ $# -eq 1 ] || die "Usage: sprint-metrics.sh init <sprint-id>"
    sprint_id="$1"
    cat > "$METRICS_FILE" <<ENDJSON
{
  "sprintId": "${sprint_id}",
  "startedAt": "${NOW}",
  "completedAt": null,
  "phases": [],
  "items": [],
  "totals": {
    "planned": 0,
    "completed": 0,
    "rework": 0,
    "blocked": 0,
    "scopeChanges": 0,
    "contextCycles": 0
  }
}
ENDJSON
    ;;

  item-start)
    [ $# -eq 1 ] || die "Usage: sprint-metrics.sh item-start <item-id>"
    item_id="$1"

    # Check if item already exists
    if command -v jq >/dev/null 2>&1; then
      exists=$(jq -r --arg id "$item_id" '.items[] | select(.id == $id) | .id' "$METRICS_FILE")
      if [ -n "$exists" ]; then
        # Item already started — skip
        exit 0
      fi
      # Add new item and increment planned count
      jq --arg id "$item_id" --arg ts "$NOW" '
        .items += [{"id": $id, "startedAt": $ts, "completedAt": null, "contextCycles": 0, "reviewFindings": 0, "reworkCount": 0, "transitions": []}]
        | .totals.planned += 1
      ' "$METRICS_FILE" > "${METRICS_FILE}.tmp" && mv "${METRICS_FILE}.tmp" "$METRICS_FILE"
    else
      die "jq is required for sprint-metrics.sh"
    fi
    ;;

  item-complete)
    [ $# -eq 1 ] || die "Usage: sprint-metrics.sh item-complete <item-id>"
    item_id="$1"

    jq --arg id "$item_id" --arg ts "$NOW" '
      if (.items | any(.id == $id)) then
        .items = [.items[] | if .id == $id then .completedAt = $ts else . end]
        | .totals.completed += 1
      else . end
    ' "$METRICS_FILE" > "${METRICS_FILE}.tmp" && mv "${METRICS_FILE}.tmp" "$METRICS_FILE"
    ;;

  cycle)
    [ $# -eq 1 ] || die "Usage: sprint-metrics.sh cycle <item-id>"
    item_id="$1"

    jq --arg id "$item_id" '
      if (.items | any(.id == $id)) then
        .items = [.items[] | if .id == $id then .contextCycles += 1 else . end]
        | .totals.contextCycles += 1
      else . end
    ' "$METRICS_FILE" > "${METRICS_FILE}.tmp" && mv "${METRICS_FILE}.tmp" "$METRICS_FILE"
    ;;

  rework)
    [ $# -eq 1 ] || die "Usage: sprint-metrics.sh rework <item-id>"
    item_id="$1"

    jq --arg id "$item_id" '
      if (.items | any(.id == $id)) then
        .items = [.items[] | if .id == $id then .reworkCount += 1 else . end]
        | .totals.rework += 1
      else . end
    ' "$METRICS_FILE" > "${METRICS_FILE}.tmp" && mv "${METRICS_FILE}.tmp" "$METRICS_FILE"
    ;;

  phase)
    [ $# -eq 1 ] || die "Usage: sprint-metrics.sh phase <phase-name>"
    phase_name="$1"

    jq --arg p "$phase_name" --arg ts "$NOW" '
      .phases = ((.phases // []) + [{"phase": $p, "at": $ts}])
    ' "$METRICS_FILE" > "${METRICS_FILE}.tmp" && mv "${METRICS_FILE}.tmp" "$METRICS_FILE"
    ;;

  status)
    [ $# -eq 2 ] || die "Usage: sprint-metrics.sh status <item-id> <value>"
    item_id="$1"
    status_value="$2"

    # Auto-create the item entry (same shape as item-start) on first status,
    # then append the transition. totals.planned increments only on creation.
    jq --arg id "$item_id" --arg val "$status_value" --arg ts "$NOW" '
      (if (.items | any(.id == $id)) then .
       else
         .items += [{"id": $id, "startedAt": $ts, "completedAt": null, "contextCycles": 0, "reviewFindings": 0, "reworkCount": 0, "transitions": []}]
         | .totals.planned += 1
       end)
      | .items = [.items[] | if .id == $id then .transitions = ((.transitions // []) + [{"status": $val, "at": $ts}]) else . end]
    ' "$METRICS_FILE" > "${METRICS_FILE}.tmp" && mv "${METRICS_FILE}.tmp" "$METRICS_FILE"
    ;;

  finalize)
    jq --arg ts "$NOW" '
      .completedAt = $ts
      | .totals.completed = ([.items[] | select(.completedAt != null)] | length)
      | .totals.planned = (.items | length)
    ' "$METRICS_FILE" > "${METRICS_FILE}.tmp" && mv "${METRICS_FILE}.tmp" "$METRICS_FILE"
    ;;

  *)
    die "Unknown subcommand '${subcmd}'"
    ;;
esac
