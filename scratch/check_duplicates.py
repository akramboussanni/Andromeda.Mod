
import os
import re
from collections import defaultdict

def find_patches(base_dir):
    # Regex to find [HarmonyPatch(typeof(TYPE), "METHOD")]
    # This also catches [HarmonyPatch(typeof(TYPE), "METHOD", ...)]
    patch_regex = re.compile(r'\[HarmonyPatch\(\s*typeof\(([^)]+)\)\s*,\s*"([^"]+)"', re.IGNORECASE)
    
    # Also handle class-level [HarmonyPatch(typeof(TYPE))] followed by method-level [HarmonyPatch("METHOD")]
    class_type_regex = re.compile(r'\[HarmonyPatch\(\s*typeof\(([^)]+)\)\s*\)\]', re.IGNORECASE)
    method_name_regex = re.compile(r'\[HarmonyPatch\(\s*"([^"]+)"\s*\)\]', re.IGNORECASE)

    targets = defaultdict(list)

    for root, dirs, files in os.walk(base_dir):
        for file in files:
            if file.endswith(".cs"):
                path = os.path.join(root, file)
                with open(path, 'r', encoding='utf-8', errors='ignore') as f:
                    content = f.read()
                    
                    # 1. Direct Class-level or Method-level combined patches
                    for match in patch_regex.finditer(content):
                        type_name = match.group(1).strip()
                        method_name = match.group(2).strip()
                        targets[f"{type_name}.{method_name}"].append(path)
                        
                    # 2. Split patches (Class type + Method name)
                    # Note: This is simpler and might miss some complex cases, but covers most Andromeda patches.
                    # We look for a class that has the type attribute then look for method attributes inside it.
                    matches = list(class_type_regex.finditer(content))
                    for i, match in enumerate(matches):
                        type_name = match.group(1).strip()
                        # Find the start of the next class patch or end of file
                        start = match.end()
                        end = matches[i+1].start() if i+1 < len(matches) else len(content)
                        block = content[start:end]
                        for m_match in method_name_regex.finditer(block):
                            method_name = m_match.group(1).strip()
                            # Check if the method also has a type in its own attribute (already handled by regex 1)
                            if f'typeof({type_name})' not in m_match.group(0):
                                targets[f"{type_name}.{method_name}"].append(path)

    return targets

def run():
    base_dir = r"c:\Users\abous\RiderProjects\Andromeda\Andromeda.Mod\Patches"
    all_targets = find_patches(base_dir)
    
    duplicates = {k: v for k, v in all_targets.items() if len(v) > 1}
    
    if not duplicates:
        print("No duplicate patches found.")
    else:
        print("Duplicate patches found:")
        for target, files in duplicates.items():
            print(f"Target: {target}")
            for f in files:
                print(f"  - {f}")

if __name__ == "__main__":
    run()
