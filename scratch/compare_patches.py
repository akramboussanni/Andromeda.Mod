
import os
import re
from collections import defaultdict

def extract_patches(dir_path):
    patches = {}
    patch_attr_re = re.compile(r'\[HarmonyPatch\(([^)]+)\)\]')
    class_re = re.compile(r'public (?:static )?class (\w+)')
    
    for filename in os.listdir(dir_path):
        if not filename.endswith(".cs"): continue
        path = os.path.join(dir_path, filename)
        
        # Try different encodings
        content = None
        for enc in ['utf-16', 'utf-8', 'cp1252']:
            try:
                with open(path, 'r', encoding=enc) as f:
                    content = f.read()
                    break
            except:
                continue
        if content is None: continue
            
        # Split by class to find individual patches
        # This is a bit simplistic but usually works for Harmony patch files
        lines = content.splitlines()
        current_class = None
        current_attrs = []
        class_lines = []
        is_in_class = False
        brace_count = 0
        
        for line in lines:
            attr_match = patch_attr_re.search(line)
            if attr_match:
                current_attrs.append(attr_match.group(1).strip())
                
            class_match = class_re.search(line)
            if class_match:
                current_class = class_match.group(1)
                is_in_class = True
                brace_count = 0
                class_lines = [line]
                continue
            
            if is_in_class:
                class_lines.append(line)
                brace_count += line.count('{')
                brace_count -= line.count('}')
                
                if brace_count <= 0 and '{' in "".join(class_lines):
                    # End of class
                    patch_id = current_class
                    patches[patch_id] = {
                        "class": current_class,
                        "targets": current_attrs,
                        "code": "\n".join(class_lines),
                        "file": filename
                    }
                    current_class = None
                    current_attrs = []
                    class_lines = []
                    is_in_class = False
        
    return patches

def compare():
    v011_dir = r"C:\Users\abous\.gemini\antigravity\brain\5a4aa6dd-f2ee-4f75-97c6-fc858b7f9ec9\scratch\patch_compare\v011"
    head_dir = r"C:\Users\abous\.gemini\antigravity\brain\5a4aa6dd-f2ee-4f75-97c6-fc858b7f9ec9\scratch\patch_compare\head"
    
    v011_patches = extract_patches(v011_dir)
    head_patches = extract_patches(head_dir)
    
    results = []
    
    all_names = sorted(set(v011_patches.keys()) | set(head_patches.keys()))
    
    for name in all_names:
        v011 = v011_patches.get(name)
        head = head_patches.get(name)
        
        if v011 and not head:
            results.append(f"MISSING: class {name} (was in {v011['file']})")
            continue
            
        if not v011 and head:
            results.append(f"NEW: class {name} (in {head['file']})")
            continue
            
        # Both exist - compare logic
        # Strip whitespace for comparison
        v_code = re.sub(r'\s+', ' ', v011['code']).strip()
        h_code = re.sub(r'\s+', ' ', head['code']).strip()
        
        if v_code != h_code:
            results.append(f"CHANGED: class {name} ({v011['file']} -> {head['file']})")
            # Highlight target changes
            if v011['targets'] != head['targets']:
                results.append(f"  TARGETS changed: {v011['targets']} -> {head['targets']}")
        else:
            # results.append(f"SAME: {name}")
            pass
            
    print("\n".join(results))

if __name__ == "__main__":
    compare()
