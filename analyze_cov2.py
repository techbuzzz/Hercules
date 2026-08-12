import xml.etree.ElementTree as ET

# Parse cobertura from latest run
path = r'C:\Sources\GitHub\Hercules\tests\Hercules.Agent.Tests\TestResults\37cb8bef-c053-4e47-a46e-e7b3cef337ea\coverage.cobertura.xml'
tree = ET.parse(path)
root = tree.getroot()
ns = {'cobertura': 'http://cobertura.sourceforge.net/xml/1.1'}

# Summary line coverage
summary = root.find('.//cobertura:line-rate', ns)
print(f"Line rate: {float(summary.text or 0)*100:.1f}%")

# Per-class coverage
classes = root.findall('.//cobertura:class', ns)
results = []
for cls in classes:
    name = cls.attrib.get('name', '')
    if 'Hercules.' not in name and '/Hercules.dll' not in cls.attrib.get('filename', ''):
        continue
    lr = cls.find('cobertura:line-rate', ns)
    br = cls.find('cobertura:branch-rate', ns)
    lc = cls.find('cobertura:lines', ns)
    covered = int(lc.attrib.get('covered', 0)) if lc is not None else 0
    total = int(lc.attrib.get('total', 0)) if lc is not None else 0
    pct = covered/total*100 if total else 0
    short = name.split('/')[-1] if '/' in name else name
    results.append((pct, short, covered, total))

results.sort(key=lambda x: x[0])
for pct, name, cov, tot in results[:50]:
    bar = '#' * max(1, int(pct/5))
    print(f"{pct:5.1f}% {bar:<20} {name:<55} {cov:4d}/{tot:4d}")
