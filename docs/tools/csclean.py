#!/usr/bin/env python3
"""csclean.py <file.cs> [method-name ...]

Strip KSP's obfuscation junk from ilspycmd output:
  while (true) { switch (N) { case 0: continue; } break; }
  if (1 == 0) { __ldtoken(...); }
With method names, print only those methods (all overloads); otherwise the whole file.
"""
import re, sys
src = open(sys.argv[1], errors='replace').read()
src = re.sub(r'[ \t]*while \(true\)\s*\{\s*switch \(\d+\)\s*\{\s*case 0:\s*continue;\s*\}\s*break;\s*\}\n', '', src)
src = re.sub(r'[ \t]*if \(1 == 0\)\s*\{\s*__ldtoken\([^;]*\);\s*\}\n', '', src)
names = sys.argv[2:]
if not names:
    sys.stdout.write(src); sys.exit()
lines = src.split('\n')
i = 0
while i < len(lines):
    l = lines[i]
    m = re.match(r'^(\t+)(?:\[.*\]\s*)?(?:public|private|protected|internal|static|override|virtual|sealed|new|extern|unsafe|async|\s)+[\w<>\[\],.? ]+?\b(\w+)\s*\(.*\)\s*$', l)
    if m and m.group(2) in names and i + 1 < len(lines) and lines[i + 1] == m.group(1) + '{':
        ind = m.group(1)
        j = i + 1
        while lines[j] != ind + '}':
            j += 1
        print('\n'.join(lines[i:j + 1])); print()
        i = j
    i += 1
