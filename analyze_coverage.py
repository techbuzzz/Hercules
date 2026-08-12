import xml.etree.ElementTree as ET
from collections import defaultdict

tree = ET.parse(r'C:\Sources\GitHub\Hercules\tests\Hercules.Agent.Tests\TestResults\e3092e9b-79c4-43df-b910-3e1c6ebfe2e6\coverage.opencover.xml')
root = tree.getroot()
modules = root.findall('Modules/Module')
summary = root.find('Summary')
print(f"Summary: {summary.attrib}")

m = modules[0]
files = m.findall('Files/File')
print(f"{len(files)} files tracked")

# Sequence points at module level
sps = m.findall('SequencePoints/SequencePoint')
print(f"{len(sps)} sequence points at module level")

# Group by file
file_data = defaultdict(lambda: [0, 0])
for sp in sps:
    uid = sp.attrib.get('fid')
    vc = int(sp.attrib.get('vc', 0))
    file_data[uid][0] += vc
    file_data[uid][1] += 1

file_results = []
for f in files:
    uid = f.attrib.get('uid')
    path = f.attrib.get('fullPath', '')
    name = path.split('\\')[-1]
    visited, total = file_data.get(uid, [0, 0])
    pct = visited / total * 100 if total else 0
    file_results.append((pct, name, visited, total, path))

file_results.sort(key=lambda x: x[0])
print("\nFile-level coverage (lowest first):")
for pct, name, v, t, path in file_results[:60]:
    bar = '#' * max(1, int(pct/5))
    print(f"{pct:5.1f}% {bar:<20} {name:<45} {v:4d}/{t:4d}")
