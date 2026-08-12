import xml.etree.ElementTree as ET

# Try latest cobertura
import glob, os
results_dir = r'C:\Sources\GitHub\Hercules\tests\Hercules.Agent.Tests\TestResults'
dirs = sorted([d for d in os.listdir(results_dir) if os.path.isdir(os.path.join(results_dir, d))], reverse=True)
for d in dirs[:3]:
    cov_files = [f for f in os.listdir(os.path.join(results_dir, d)) if 'coverage' in f]
    print(f"{d}: {cov_files}")
