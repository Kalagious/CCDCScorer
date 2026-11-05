#!/bin/bash
# prank_05_sudo_lecture.sh — Red team warning displayed every time sudo is used
# Undo: bash prank_05_sudo_lecture.sh --remove

if [ "$1" = "--remove" ]; then
  sed -i '/lecture=always/d' /etc/sudoers
  sed -i '/sudo_lecture/d' /etc/sudoers
  rm -f /etc/sudo_lecture
  echo "[+] Sudo lecture removed"
  exit 0
fi

grep -q 'lecture=always' /etc/sudoers || echo 'Defaults lecture=always' >> /etc/sudoers
grep -q 'sudo_lecture'   /etc/sudoers || echo 'Defaults lecture_file=/etc/sudo_lecture' >> /etc/sudoers

cat > /etc/sudo_lecture <<'EOF'

  WARNING: All sudo activity is being monitored and forwarded
  to the RED TEAM in real time. We appreciate the help. <3

EOF

echo "[+] Sudo lecture installed — shown every time any user runs sudo"
