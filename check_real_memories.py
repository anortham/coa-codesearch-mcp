#!/usr/bin/env python3
import sqlite3
import json
import re

db_path = r"C:\source\COA Roslyn MCP\.codesearch\memories.db"

print(f"Checking for real memories in: {db_path}")
print("-" * 80)

# GUID pattern
guid_pattern = re.compile(r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')

try:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    
    # Count records with GUID vs file path IDs
    cursor.execute("SELECT id, scope FROM memories")
    records = cursor.fetchall()
    
    guid_records = []
    path_records = []
    
    for id_val, scope in records:
        if guid_pattern.match(id_val):
            guid_records.append((id_val, scope))
        else:
            path_records.append((id_val, scope))
    
    print(f"Total records: {len(records)}")
    print(f"Records with GUID IDs: {len(guid_records)}")
    print(f"Records with file path IDs: {len(path_records)}")
    print("-" * 80)
    
    # Show GUID records if any
    if guid_records:
        print("Records with proper GUID IDs:")
        for id_val, scope in guid_records[:5]:
            print(f"  {id_val} - {scope}")
    
    # Show the one ArchitecturalDecision
    cursor.execute("SELECT id, content, json_data FROM memories WHERE scope='ArchitecturalDecision'")
    arch_records = cursor.fetchall()
    print("\nArchitecturalDecision records:")
    for id_val, content, json_data in arch_records:
        print(f"  ID: {id_val}")
        print(f"  Content: {content[:200]}...")
        
    conn.close()
    
except Exception as e:
    print(f"Error: {e}")