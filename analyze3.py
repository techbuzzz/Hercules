import xml.etree.ElementTree as ET
from collections import defaultdict

tree = ET.parse(r'C:\Sources\GitHub\Hercules\tests\Hercules.Agent.Tests\TestResults\37cb8bef-c053-4e47-a46e-e7b3cef337ea\coverage.opencover.xml')
root = tree.getroot()
m = root.findall('Modules/Module')[0]

files = m.findall('Files/File')
file_map = {int(f.attrib['uid']): f.attrib['fullPath'] for f in files}

# SequencePoints at module level
sps = m.findall('SequencePoints/SequencePoint')
print(f"Found {len(sps)} sequence points at module level")

file_data = defaultdict(lambda: [0, 0])
for sp in sps:
    fid = int(sp.attrib.get('fid', 0))
    vc = int(sp.attrib.get('vc', 0))
    file_data[fid][0] += vc
    file_data[fid][1] += 1

file_results = []
for uid, path in file_map.items():
    name = path.split('\\')[-1]
    visited, total = file_data.get(uid, [0, 0])
    pct = visited / total * 100 if total else 0
    file_results.append((pct, name, visited, total, path))

file_results.sort(key=lambda x: x[0])
print("\nFile-level coverage (lowest first):")
for pct, name, v, t, path in file_results[:60]:
    bar = '#' * max(1, int(pct/5))
    print(f"{pct:5.1f}% {bar:<20} {name:<45} {v:4d}/{t:4d}")
