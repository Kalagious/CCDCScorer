#!/bin/bash
# prank_09_calling_card.sh — Drops a taunting note in /tmp and /home of every user
# Undo: bash prank_09_calling_card.sh --remove

if [ "$1" = "--remove" ]; then
  rm -f /tmp/you_got_pwned.txt
  for home in /home/*/; do
    rm -f "${home}you_got_pwned.txt"
  done
  rm -f /root/you_got_pwned.txt
  echo "[+] Calling cards removed"
  exit 0
fi

CARD=$(cat <<'EOF'
     _____________________
    |                     |
    |   YOU GOT PWNED     |
    |                     |
    |  Initial access via |
    |  anonymous FTP :)   |
    |_____________________|

  Better luck next time, blue team.
  Hint: check /var/www/html/backup/

  - Red Team <3
EOF
)

echo "$CARD" > /tmp/you_got_pwned.txt
echo "$CARD" > /root/you_got_pwned.txt

for home in /home/*/; do
  echo "$CARD" > "${home}you_got_pwned.txt"
  chown "$(basename "$home"):$(basename "$home")" "${home}you_got_pwned.txt" 2>/dev/null || true
done

echo "[+] Calling cards dropped in /tmp, /root, and all home directories"
