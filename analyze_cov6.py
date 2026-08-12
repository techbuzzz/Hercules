import xml.etree.ElementTree as ET

path = r'C:\Sources\GitHub\Hercules\tests\Hercules.Agent.Tests\TestResults\fed15250-fa86-4a01-a1ec-0accb052688d\coverage.cobertura.xml'
tree = ET.parse(path)
root = tree.getroot()

line_rate = root.attrib.get('line-rate')
branch_rate = root.attrib.get('branch-rate')
print(f"Line coverage: {float(line_rate or 0)*100:.1f}%")
print(f"Branch coverage: {float(branch_rate or 0)*100:.1f}%")

packages = root.findall('.//package')
total_lines = 0
total_covered = 0
class_data = {}
for pkg in packages:
    for cls in pkg.findall('.//class'):
        name = cls.attrib.get('name', '')
        filename = cls.attrib.get('filename', '')
        lines = cls.find('lines')
        covered = total_cov = 0
        if lines is not None:
            for line in lines.findall('line'):
                cov = int(line.attrib.get('hits', 0))
                total_cov += 1
                covered += 1 if cov > 0 else 0
        total_lines += total_cov
        total_covered += covered
        # Short name: get last 2-3 parts
        parts = name.split('.')
        short = '.'.join(parts[-2:]) if len(parts) >= 2 else name
        pct = covered/total_cov*100 if total_cov else 0
        class_data[short] = (pct, covered, total_cov)

items = sorted(class_data.items(), key=lambda x: x[1][0])
print(f"\nPer-class coverage (lowest first, top 50):")
for name, (pct, cov, tot) in items[:50]:
    bar = '#' * max(1, int(pct/5))
    print(f"{pct:5.1f}% {bar:<20} {name:<55} {cov:4d}/{tot:4d}")
overall = total_covered/total_lines*100 if total_lines else 0
print(f"\nOverall coverage: {total_covered}/{total_lines} = {overall:.1f}%")
