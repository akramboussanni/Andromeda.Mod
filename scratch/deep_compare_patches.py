
import os
import re
import difflib

def extract_methods(class_code):
    # Very crude extractor for methods like Prefix, Postfix, etc.
    methods = {}
    method_re = re.compile(r'public (?:static )?(?:\w+ )?(\w+)\s*\([^)]*\)\s*\{')
    
    # Split by lines and find the start of methods
    lines = class_code.splitlines()
    for i, line in enumerate(lines):
        match = method_re.search(line)
        if match:
            method_name = match.group(1)
            # Find the end of the method by brace counting
            brace_count = 1
            method_lines = [line]
            for j in range(i+1, len(lines)):
                m_line = lines[j]
                method_lines.append(m_line)
                brace_count += m_line.count('{')
                brace_count -= m_line.count('}')
                if brace_count <= 0:
                    break
            methods[method_name] = "\n".join(method_lines)
    return methods

def get_class_code(dir_path, class_name):
    for filename in os.listdir(dir_path):
        path = os.path.join(dir_path, filename)
        content = None
        for enc in ['utf-16', 'utf-8', 'cp1252']:
            try:
                with open(path, 'r', encoding=enc) as f:
                    content = f.read()
                    break
            except: continue
        if not content: continue
        
        # Simple class extractor
        class_match = re.search(r'public (?:static )?class ' + class_name + r'\s*\{', content)
        if class_match:
            start = class_match.start()
            brace_count = 1
            class_lines = []
            lines = content[start:].splitlines()
            for line in lines:
                class_lines.append(line)
                if '{' in line: brace_count += line.count('{')
                if '}' in line: brace_count -= line.count('}')
                if brace_count <= 1 and '}' in line and len(class_lines) > 1: # simplistic
                    # Actually we need to be more careful, but for Harmony classes this works
                    pass
            # Better brace count
            brace_count = 0
            found_start = False
            result_lines = []
            for line in lines:
                result_lines.append(line)
                if '{' in line:
                    brace_count += line.count('{')
                    found_start = True
                if '}' in line:
                    brace_count -= line.count('}')
                if found_start and brace_count == 0:
                    break
            return "\n".join(result_lines)
    return None

def deep_compare():
    v011_dir = r"C:\Users\abous\R Gemini\antigravity\brain\5a4aa6dd-f2ee-4f75-97c6-fc858b7f9ec9\scratch\patch_compare\v011"
    # Wait, the path has spaces or different names? 
    # Let me use the known path from previous command
    v011_dir = r"C:\Users\abous\.gemini\antigravity\brain\5a4aa6dd-f2ee-4f75-97c6-fc858b7f9ec9\scratch\patch_compare\v011"
    head_dir = r"C:\Users\abous\.gemini\antigravity\brain\5a4aa6dd-f2ee-4f75-97c6-fc858b7f9ec9\scratch\patch_compare\head"
    
    classes_to_check = [
        "EntityBaseSendReliablePatch", 
        "EntityBaseSendReliableToRoomPatch",
        "EntityBaseSendUnreliablePatch",
        "EntityBaseSendUnreliableToRoomPatch",
        "ProgramServerPatch",
        "AndromedaClientServerGuardPatch",
        "ScenesLoadPathSafetyPatch",
        "VoiceUIHUDSafetyPatch",
        "VoiceClientSafetyPatch",
        "LobbyMaxPlayersPatch",
        "AndromedaPhaseClockDesyncPatch",
        "AndromedaSelectionDesyncPatch"
    ]
    
    for name in classes_to_check:
        print(f"=== Deep Dive: {name} ===")
        v_code = get_class_code(v011_dir, name)
        h_code = get_class_code(head_dir, name)
        
        if not v_code or not h_code:
            print(f"Skipping {name} (missing in one version)")
            continue
            
        v_methods = extract_methods(v_code)
        h_methods = extract_methods(h_code)
        
        all_methods = set(v_methods.keys()) | set(h_methods.keys())
        for m in all_methods:
            v_m = v_methods.get(m, "")
            h_m = h_methods.get(m, "")
            
            # Normalize for comparison
            vn = re.sub(r'\s+', ' ', v_m).strip()
            hn = re.sub(r'\s+', ' ', h_m).strip()
            
            if vn != hn:
                print(f"  METHOD CHANGED: {m}")
                diff = difflib.unified_diff(
                    v_m.splitlines(),
                    h_m.splitlines(),
                    fromfile='v0.11.1',
                    tofile='HEAD',
                    lineterm=''
                )
                for line in diff:
                    print(f"    {line}")
            else:
                # print(f"  METHOD SAME: {m}")
                pass

if __name__ == "__main__":
    deep_compare()
