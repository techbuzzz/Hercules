import xml.etree.ElementTree as ET
from collections import defaultdict

tree = ET.parse(r'C:\Sources\GitHub\Hercules\tests\Hercules.Agent.Tests\TestResults\e3092e9b-79c4-43df-b910-3e1c6ebfe2e6\coverage.opencover.xml')
root = tree.getroot()
modules = root.findall('Modules/Module')
m = modules[0]

# Check what children the module has
print("Module children:", [c.tag for c in m])
print("Module summary:", m.find('Summary').attrib if m.find('Summary') is not None else 'none')

# Check if SequencePoints are inside Methods
classes = m.findall('Classes/Class')
if classes:
    c = classes[0]
    print("\nFirst class children:", [c2.tag for c2 in c])
    for mth in c.findall('Methods/Method'):
        sps = mth.findall('SequencePoint')
        if sps:
            print(f"  Method has {len(sps)} sequence points")
            print(f"  First SP: {sps[0].attrib}")
            break
    else:
        print("  No sequence points in any method of first class")

# Maybe sequence points are in a different location - check all tags
all_tags = set()
for el in m.iter():
    all_tags.add(el.tag)
print("\nAll tags in module:", sorted(all_tags))
