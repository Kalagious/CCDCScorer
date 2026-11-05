#!/bin/bash
# prank_01_motd.sh — ASCII art greeting shown on every SSH login
# Undo: bash prank_01_motd.sh --remove

if [ "$1" = "--remove" ]; then
  truncate -s 0 /etc/motd
  echo "[+] MOTD cleared"
  exit 0
fi

cat > /etc/motd <<'EOF'


    ██████╗ ██╗    ██╗███╗   ██╗███████╗██████╗
    ██╔══██╗██║    ██║████╗  ██║██╔════╝██╔══██╗
    ██║  ██║██║ █╗ ██║██╔██╗ ██║█████╗  ██║  ██║
    ██║  ██║██║███╗██║██║╚██╗██║██╔══╝  ██║  ██║
    ██████╔╝╚███╔███╔╝██║ ╚████║███████╗██████╔╝
    ╚═════╝  ╚══╝╚══╝ ╚═╝  ╚═══╝╚══════╝╚═════╝

         lol we were already here  :)
         flag{y0u_sh0uld_h4v3_p4tch3d_f4st3r}

EOF

echo "[+] MOTD installed — visible on every SSH login"
