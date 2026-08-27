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
# `on <date> <command>` is completed all the way through: the command list after
# the date, then that command's own candidates taken from the day the date names.

# Fills COMPREPLY from a newline-separated candidate list. Task names contain
# spaces, so the word readline hands us may be quoted or backslash-escaped:
# strip that to match, then put it back on the result.
_today_reply() {
    local names=$1 cur=$2 quote="" typed=$2 i

    [[ -n $names ]] || return

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

_today() {
    local cur cmd names i argc base day

    cur=${COMP_WORDS[COMP_CWORD]}

    if ((COMP_CWORD == 1)); then
        _today_reply "$(today complete commands 2>/dev/null)" "$cur"
        return
    fi

    # `on <date> <command> ...` shifts a whole command two words to the right and
    # points it at another day. Past the date and the command name, everything
    # below treats it as though it had been typed on its own, with $day carried
    # along so the candidates come from that day rather than from today.
    base=1
    day=""
    if [[ ${COMP_WORDS[1]} == on ]]; then
        if ((COMP_CWORD == 2)); then
            _today_reply "$(today complete on 0 2>/dev/null)" "$cur"
            return
        fi
        if ((COMP_CWORD == 3)); then
            _today_reply "$(today complete on 1 2>/dev/null)" "$cur"
            return
        fi
        day=${COMP_WORDS[2]}
        base=3
    fi

    cmd=${COMP_WORDS[base]}

    case $cmd in
    end | did | rm | show | summary | completion) ;;
    *) return ;;
    esac

    # Only the first argument is completable -- in `end <what> [when]` the second
    # is a time, not another task name. `summary <from> <to>` is the exception: a
    # day goes in either position.
    argc=0
    for ((i = base + 1; i < COMP_CWORD; i++)); do
        [[ ${COMP_WORDS[i]} == -* ]] || ((argc++))
    done
    if [[ $cmd == summary ]]; then
        ((argc <= 1)) || return
    else
        ((argc == 0)) || return
    fi

    # $argc goes along so `complete` can tell which argument is being completed:
    # `summary` offers its span words in the first position only.
    if [[ -n $day ]]; then
        names=$(today complete "$cmd" "$argc" "$day" 2>/dev/null) || return
    else
        names=$(today complete "$cmd" "$argc" 2>/dev/null) || return
    fi

    _today_reply "$names" "$cur"
}

complete -F _today today
