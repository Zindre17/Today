# bash completion for the `today` dotnet tool.
#
# Install by copying (or symlinking) this file to
#   ~/.local/share/bash-completion/completions/today
# and starting a new shell.
#
# Candidates come from `today complete ...`, so the shell never has its own idea
# of what the commands are, and `today end <TAB>` offers exactly the tasks that
# are running right now -- `today rm <TAB>`, everything on the day,
# `today did <TAB>`, the ones already finished, and `today on <TAB>`, the days
# in history.
#
# Only the first argument of a command is completed, so `today on <date> end <TAB>`
# offers nothing rather than offering today's tasks for another day's command.

_today() {
    local cur cmd names i argc
    cur=${COMP_WORDS[COMP_CWORD]}
    cmd=${COMP_WORDS[1]}

    if ((COMP_CWORD == 1)); then
        mapfile -t COMPREPLY < <(compgen -W "$(today complete commands 2>/dev/null)" -- "$cur")
        return
    fi

    [[ $cmd == end || $cmd == rm || $cmd == did || $cmd == show || $cmd == completion || $cmd == on ]] || return

    # `end <what> [when]` and `rm <what>` -- only the first argument is a task name.
    argc=0
    for ((i = 2; i < COMP_CWORD; i++)); do
        [[ ${COMP_WORDS[i]} == -* ]] || ((argc++))
    done
    ((argc == 0)) || return

    names=$(today complete "$cmd" 2>/dev/null) || return
    [[ -n $names ]] || return

    # Task names contain spaces, so the word readline hands us may be quoted or
    # backslash-escaped. Strip that to match, then put it back on the result.
    local quote="" typed=$cur
    case $typed in
    \"* | \'*)
        quote=${typed:0:1}
        typed=${typed:1}
        ;;
    *)
        typed=${typed//\\ / }
        ;;
    esac

    local IFS=$'\n'
    mapfile -t COMPREPLY < <(compgen -W "$names" -- "$typed")

    if [[ -z $quote ]]; then
        for i in "${!COMPREPLY[@]}"; do
            COMPREPLY[i]=$(printf '%q' "${COMPREPLY[i]}")
        done
    fi
}

complete -F _today today
