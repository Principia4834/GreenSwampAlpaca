#!/bin/bash
set -euo pipefail
export PATH=/usr/bin:/bin:/usr/sbin:/sbin

LABEL="com.greenswamp.alpaca.server"
WORK_DIR=""

finish() {
	local result=$?
	trap - EXIT
	if [ -n "$WORK_DIR" ]; then
		rm -f "$WORK_DIR/agent.plist" "$WORK_DIR/health-error"
		rmdir "$WORK_DIR"
	fi
	if [ "$result" -ne 0 ]; then
		printf '\nSetup did not complete successfully. Read the error above.\n' >&2
	fi
	if [ -t 0 ]; then
		read -r -p "Press Return to close this window... " answer || :
	fi
	exit "$result"
}
trap finish EXIT
trap 'printf "\nERROR: Command failed at line %s.\n" "$LINENO" >&2' ERR

fail() {
	printf '\nERROR: %s\n' "$1" >&2
	exit 1
}

confirm() {
	local answer
	read -r -p "Type $1 to accept, or press Return to cancel: " answer
	if [ "$answer" != "$1" ]; then
		printf 'Cancelled. No changes made.\n'
		exit 0
	fi
}

[ "$(uname -s)" = "Darwin" ] || fail "This helper runs only on macOS."
[ "$(id -u)" -ne 0 ] || fail "Run this as your normal logged-in account, not with sudo."
[ -t 0 ] || fail "Open this command file in Terminal so you can review and accept the actions."

DOMAIN="gui/$(id -u)"
TARGET="$DOMAIN/$LABEL"
AGENTS_DIR="$HOME/Library/LaunchAgents"
PLIST="$AGENTS_DIR/$LABEL.plist"
LOG_DIR="$HOME/Library/Logs/GreenSwampAlpaca"
launchctl print "$DOMAIN" >/dev/null

printf '\nGreenSwamp Alpaca Server - Background Startup\n\n'
printf '1. Enable startup at login\n2. Disable startup and stop the background server\n3. Show status\n4. Cancel\n\n'
read -r -p "Choose 1, 2, 3 or 4: " action

case "$action" in
	1)
		SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -P)"
		APP_PATH="$SCRIPT_DIR/GreenSwamp Alpaca Server.app"
		LAUNCHER="$APP_PATH/Contents/MacOS/GreenSwampAlpacaLauncher"
		case "$SCRIPT_DIR" in
			/Applications/*|"$HOME/Applications/"*) ;;
			*) fail "Copy the entire GreenSwamp Alpaca Server folder to Applications (or your personal Applications folder), then run its helper there. Do not run it from the DMG." ;;
		esac
		[ -x "$LAUNCHER" ] || fail "The application is missing or incomplete. Keep the helper next to GreenSwamp Alpaca Server.app."
		[ ! -L "$AGENTS_DIR" ] && [ ! -L "$PLIST" ] && [ ! -L "$LOG_DIR" ] || fail "The agent or log location is a symbolic link. Review it before proceeding."
		if launchctl print "$TARGET" >/dev/null 2>&1; then
			fail "This agent is already loaded. Use Status, or Disable before enabling it again."
		fi
		if processes=$(pgrep -fl 'GreenSwamp[.]Alpaca[.]Server|GreenSwampAlpacaLauncher'); then
			printf '%s\n' "$processes"
			fail "A server process is already running. Stop it yourself before enabling the agent; no processes have been terminated."
		else
			result=$?
			[ "$result" -eq 1 ] || fail "Unable to check for running server processes."
		fi
		if listeners=$(lsof -nP -iTCP:31416 -sTCP:LISTEN); then
			printf '%s\n' "$listeners"
			fail "Port 31416 is already in use. Resolve this before enabling the agent."
		else
			result=$?
			[ "$result" -eq 1 ] || fail "Unable to check port 31416."
		fi
		if [ -e "$PLIST" ]; then
			[ "$(plutil -extract Label raw -o - "$PLIST")" = "$LABEL" ] || fail "An unrelated plist exists at $PLIST. It will not be overwritten."
		fi

		printf '\nThis will create or replace:\n  %s\n' "$PLIST"
		printf 'It will enable and start the server for your account now and at future logins.\n'
		printf 'launchd will restart it if it exits. It stops at logout; this is NOT a boot-time system daemon.\n'
		printf 'No administrator access is used. No browser will open automatically.\n'
		printf '\nWARNING: Service settings use ~/GreenSwampServer, not the interactive settings folder.\n'
		printf 'Existing settings are NOT migrated or deleted. Do not launch a second instance from Applications.\n'
		printf 'Do not move or rename the installed folder while startup is enabled.\n'
		printf 'Existing agent configuration at the path above will be replaced; logs append in:\n  %s\n' "$LOG_DIR"
		printf 'Startup checks use the default address http://localhost:31416.\n\n'
		confirm ENABLE

		umask 077
		WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/greenswamp-launchagent.XXXXXX")"
		TEMP_PLIST="$WORK_DIR/agent.plist"
		plutil -create xml1 "$TEMP_PLIST"
		plutil -insert Label -string "$LABEL" "$TEMP_PLIST"
		plutil -insert ProgramArguments -json '[]' "$TEMP_PLIST"
		plutil -insert ProgramArguments.0 -string "$LAUNCHER" "$TEMP_PLIST"
		plutil -insert ProgramArguments.1 -string '--service' "$TEMP_PLIST"
		plutil -insert RunAtLoad -bool true "$TEMP_PLIST"
		plutil -insert KeepAlive -bool true "$TEMP_PLIST"
		plutil -insert StandardOutPath -string "$LOG_DIR/launchd.out.log" "$TEMP_PLIST"
		plutil -insert StandardErrorPath -string "$LOG_DIR/launchd.err.log" "$TEMP_PLIST"
		plutil -lint "$TEMP_PLIST"
		mkdir -p "$AGENTS_DIR" "$LOG_DIR"
		cp "$TEMP_PLIST" "$PLIST"
		chmod 600 "$PLIST"
		launchctl enable "$TARGET"
		printf '\nThe agent is now enabled. If startup fails, use Disable in this helper.\n'
		launchctl bootstrap "$DOMAIN" "$PLIST"

		for ((attempt=1; attempt<=15; attempt++)); do
			pid="$(launchctl kickstart -p "$TARGET")"
			case "$pid" in
				''|*[!0-9]*) fail "launchd did not return a valid process ID. Use Status to investigate." ;;
			esac
			if kill -0 "$pid" 2>/dev/null &&
				curl --noproxy '*' --fail --silent --show-error --max-time 2 \
					--output /dev/null http://127.0.0.1:31416/ 2> "$WORK_DIR/health-error" &&
				kill -0 "$pid" 2>/dev/null; then
				printf '\nBackground startup is enabled and the management UI is responding.\n'
				printf 'Open http://localhost:31416 in your browser.\n'
				exit 0
			fi
			sleep 2
		done
		launchctl print "$TARGET"
		if [ -f "$WORK_DIR/health-error" ]; then
			cat "$WORK_DIR/health-error" >&2
		fi
		fail "The agent was registered, but startup could not be verified at port 31416. Check $LOG_DIR, your configured server port, and Status. It remains enabled; use Disable to stop it."
		;;
	2)
		printf '\nThis will disable %s for your account and stop it if loaded.\n' "$LABEL"
		printf 'WARNING: Connected telescope clients will lose access to this server.\n'
		printf 'The installed app, plist, settings and logs will be kept. Other processes will not be stopped.\n\n'
		confirm DISABLE
		launchctl disable "$TARGET"
		if launchctl print "$TARGET" >/dev/null 2>&1; then
			launchctl bootout "$TARGET"
		fi
		printf '\nBackground startup is disabled and the agent is not loaded.\n'
		;;
	3)
		printf '\nPer-user launchd overrides (true means disabled):\n'
		launchctl print-disabled "$DOMAIN"
		if launchctl print "$TARGET"; then
			printf '\nManagement UI check at the default port:\n'
			curl --noproxy '*' --fail --silent --show-error --max-time 5 \
				--output /dev/null http://127.0.0.1:31416/
			printf 'The management UI is responding at http://localhost:31416.\n'
		else
			printf '\nThe agent is not loaded.\n'
		fi
		printf '\nAgent file: %s\nLaunch logs: %s\n' "$PLIST" "$LOG_DIR"
		;;
	4|'') printf 'Cancelled. No changes made.\n' ;;
	*) fail "Invalid selection. Choose an option from 1 to 4." ;;
esac
