
import os
import re

def process_file(path):
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        lines = f.readlines()
        
    changed = False
    new_lines = []
    
    # Pattern 1: if (!DedicatedServerStartup.IsServer) return ...
    # Replace with: if (!DedicatedServerStartup.IsServer && !Andromeda.Mod.Patches.EnvironmentPatch.IsHost()) return ...
    p1 = re.compile(r'if \(!DedicatedServerStartup\.IsServer\) return')
    
    # Pattern 2: return !DedicatedServerStartup.IsServer;
    # Replace with: return !(DedicatedServerStartup.IsServer || Andromeda.Mod.Patches.EnvironmentPatch.IsHost());
    p2 = re.compile(r'return !DedicatedServerStartup\.IsServer;')
    
    for line in lines:
        if p1.search(line):
            # Special case for !IsServer && !EnvironmentPatch.IsHost()
            if 'EnvironmentPatch.IsHost()' not in line:
                line = line.replace('!DedicatedServerStartup.IsServer', '!DedicatedServerStartup.IsServer && !Andromeda.Mod.Patches.EnvironmentPatch.IsHost()')
                changed = True
        elif p2.search(line):
            if 'EnvironmentPatch.IsHost()' not in line:
                line = line.replace('!DedicatedServerStartup.IsServer', '!(DedicatedServerStartup.IsServer || Andromeda.Mod.Patches.EnvironmentPatch.IsHost())')
                changed = True
        new_lines.append(line)
        
    if changed:
        with open(path, 'w', encoding='utf-8') as f:
            f.writelines(new_lines)
        return True
    return False

def run():
    base_dir = r"c:\Users\abous\RiderProjects\Andromeda\Andromeda.Mod\Patches"
    for root, dirs, files in os.walk(base_dir):
        for file in files:
            if file.endswith(".cs"):
                if process_file(os.path.join(root, file)):
                    print(f"Updated {file}")

if __name__ == "__main__":
    run()
