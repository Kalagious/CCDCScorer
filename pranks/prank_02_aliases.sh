#!/bin/bash
# prank_02_aliases.sh — Snarky shell aliases that fire for every user
# Undo: bash prank_02_aliases.sh --remove

if [ "$1" = "--remove" ]; then
  sed -i '/# PRANK_START/,/# PRANK_END/d' /etc/bash.bashrc
  echo "[+] Shell aliases removed"
  exit 0
fi

cat >> /etc/bash.bashrc <<'EOF'

# PRANK_START
alias sudo='echo "sudo: command not found... just kidding" && sleep 2 && /usr/bin/sudo'
alias ls='ls --color=auto; echo "    (and yes, we can see all of these)"'
alias vim='echo "Nice try, vim users smell" && sleep 1 && /usr/bin/vim'
alias nano='echo "A nano user... how quaint" && sleep 1 && /usr/bin/nano'
alias cat='echo "cat: reading files for the red team too..." && sleep 1 && /usr/bin/cat'
# PRANK_END
EOF

echo "[+] Shell aliases installed — fires for every interactive bash session"
