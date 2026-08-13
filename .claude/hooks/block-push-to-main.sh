#!/usr/bin/env bash
#
# PreToolUse hook: refuse Bash commands that would push to the remote's main
# branch. Exits 2 to block the tool call; anything else lets it through.
#
# Wire it up in .claude/settings.json:
#
#   {
#     "hooks": {
#       "PreToolUse": [
#         {
#           "matcher": "Bash",
#           "hooks": [
#             {
#               "type": "command",
#               "command": "$CLAUDE_PROJECT_DIR/.claude/hooks/block-push-to-main.sh"
#             }
#           ]
#         }
#       ]
#     }
#   }
#
# Set BLOCK_PUSH_BRANCH to protect a branch other than main.
#
# Note: this reads the command as text, so it errs toward blocking — a command
# that merely mentions "git push main" in a string is refused too. It cannot
# catch a push hidden behind a script or an alias it can't see.

set -uo pipefail

PROTECTED="${BLOCK_PUSH_BRANCH:-main}"

input=$(cat)

# Without a JSON parser we cannot tell what the command is, so stay out of the way.
if ! command -v jq >/dev/null 2>&1; then
    exit 0
fi

tool_name=$(printf '%s' "$input" | jq -r '.tool_name // empty')
command_line=$(printf '%s' "$input" | jq -r '.tool_input.command // empty')
cwd=$(printf '%s' "$input" | jq -r '.cwd // empty')

[ "$tool_name" = "Bash" ] || exit 0
[ -n "$command_line" ] || exit 0

block() {
    echo "Blocked by block-push-to-main.sh: this would push to '$PROTECTED' on the remote." >&2
    echo "Reason: $1" >&2
    echo "Push a different branch and open a pull request, or ask the user to push it themselves." >&2
    exit 2
}

# Split on shell separators so `cd foo && git push origin main` is still seen.
segments=$(printf '%s' "$command_line" | sed -E 's/(\&\&|\|\||;|\||\n)/\n/g')

while IFS= read -r segment; do
    # Only look at segments that actually invoke `git ... push`.
    printf '%s' "$segment" | grep -Eq '(^|[[:space:]])git([[:space:]]+-[^[:space:]]+)*[[:space:]]+push([[:space:]]|$)' || continue

    read -ra tokens <<<"$segment"

    # Collect the arguments that follow `push`.
    args=()
    seen_push=0
    for token in "${tokens[@]}"; do
        if [ "$seen_push" -eq 1 ]; then
            args+=("$token")
        elif [ "$token" = "push" ]; then
            seen_push=1
        fi
    done

    # Separate flags from the remote and refspecs.
    positional=()
    for arg in "${args[@]:-}"; do
        case "$arg" in
        "") ;;
        --all | --mirror)
            block "$arg pushes every branch, including $PROTECTED"
            ;;
        -*) ;;
        *) positional+=("$arg") ;;
        esac
    done

    # positional[0] is the remote; everything after it is a refspec.
    refspecs=("${positional[@]:1}")

    if [ "${#refspecs[@]}" -gt 0 ]; then
        for refspec in "${refspecs[@]}"; do
            dst="${refspec#+}" # a leading + is a force push
            case "$dst" in
            *:*) dst="${dst##*:}" ;; # src:dst -- only the destination matters
            esac
            dst="${dst#refs/heads/}"
            if [ "$dst" = "$PROTECTED" ]; then
                block "the refspec '$refspec' targets $PROTECTED"
            fi
        done
        continue
    fi

    # No refspec: git pushes the current branch, or wherever its upstream points.
    repo="${cwd:-$PWD}"

    branch=$(git -C "$repo" rev-parse --abbrev-ref HEAD 2>/dev/null || true)
    if [ "$branch" = "$PROTECTED" ]; then
        block "the current branch is $PROTECTED and no refspec was given"
    fi

    upstream=$(git -C "$repo" rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null || true)
    if [ -n "$upstream" ] && [ "${upstream##*/}" = "$PROTECTED" ]; then
        block "the current branch tracks $upstream"
    fi
done <<<"$segments"

exit 0
