#!/bin/sh
# Reads miles and points balances with udaan, one account at a time, and hands every
# result to the tracker. Run it once a day from cron or launchd on the machine that has udaan.
#
#   tools/udaan-balances.sh [account ...]
#
# With no account named, every account "udaan list" shows is checked.
#
# Two accounts are never checked together: a check finishes before the next one starts,
# a pause follows every live sign-in, and accounts of the same program are placed as far
# apart in the order as the list allows.
#
# Settings, all optional, from the environment:
#   TRACKER_URL     where the tracker listens                       (default http://localhost:8080)
#   UDAAN           the udaan command                               (default udaan)
#   UDAAN_MAX_AGE   reuse a result younger than this many seconds   (default 72000, 20 hours)
#   UDAAN_PAUSE     seconds to wait after a live sign-in            (default 900, 15 minutes)
#
# A result is delivered whether its check worked or not, so the tracker can say which
# account needs a look. udaan keeps its last result, so a run that could not reach the
# tracker loses nothing: the next run delivers it without signing in again.
#
# Exit code: 0 when every result was delivered, 1 otherwise.
set -u

tracker=${TRACKER_URL:-http://localhost:8080}
udaan=${UDAAN:-udaan}
max_age=${UDAAN_MAX_AGE:-72000}
pause=${UDAAN_PAUSE:-900}

say() { printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }

# Nothing signs in unless the result has somewhere to go.
if ! curl -sS -f -o /dev/null "$tracker/health"; then
    say "the tracker does not answer at $tracker; nothing was checked"
    exit 1
fi

if [ "$#" -gt 0 ]; then
    accounts=$*
else
    # "udaan list" prints one key per line. Each account is numbered within its program and
    # the list is sorted by that number, which deals the programs out in turn.
    accounts=$("$udaan" list | awk -F'"' '
        $2 == "account" { account = $4 }
        $2 == "program" && account != "" { seen[$4]++; print seen[$4], NR, account; account = "" }
    ' | sort -n -k1,1 -k2,2 | cut -d' ' -f3)
fi

if [ -z "$accounts" ]; then
    say "udaan lists no accounts"
    exit 1
fi

undelivered=0
wait_first=0
for account in $accounts; do
    if [ "$wait_first" -gt 0 ]; then
        say "waiting $wait_first seconds before $account"
        sleep "$wait_first"
    fi

    # udaan prints its result as JSON even when the check fails; its exit code is not needed here.
    result=$("$udaan" check "$account" --max-age "$max_age")

    # A result reused from udaan's own cache did not sign in anywhere, so nothing needs spacing out.
    case $result in
        *'"source": "cache"'*) wait_first=0 ;;
        *) wait_first=$pause ;;
    esac

    if reply=$(printf '%s' "$result" | curl -sS --fail-with-body -X POST \
        -H 'content-type: application/json' --data-binary @- "$tracker/api/balances/udaan"); then
        say "$account: $reply"
    else
        say "$account: not delivered. $reply"
        undelivered=1
    fi
done

exit "$undelivered"
