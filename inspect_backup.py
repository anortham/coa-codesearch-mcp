#!/usr/bin/env python3
import sqlite3
import json
from datetime import datetime

db_path = r"C:\source\COA Roslyn MCP\.codesearch\memories.db"

print(f"Inspecting SQLite backup database: {db_path}")
print("-" * 80)

try:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    
    # Check table schema
    cursor.execute("SELECT sql FROM sqlite_master WHERE type='table' AND name='memories'")
    schema = cursor.fetchone()
    if schema:
        print("Table Schema:")
        print(schema[0])
        print("-" * 80)
    
    # Count records by scope
    cursor.execute("SELECT scope, COUNT(*) FROM memories GROUP BY scope")
    scope_counts = cursor.fetchall()
    print("Records by scope:")
    for scope, count in scope_counts:
        print(f"  {scope}: {count}")
    print("-" * 80)
    
    # Show sample records
    cursor.execute("SELECT id, scope, content, json_data FROM memories LIMIT 5")
    records = cursor.fetchall()
    print(f"Sample records (showing {len(records)} of total):")
    for i, (id_val, scope, content, json_data) in enumerate(records, 1):
        print(f"\n{i}. ID: {id_val}")
        print(f"   Scope: {scope}")
        print(f"   Content preview: {content[:100]}...")
        
        # Try to parse JSON data
        try:
            data = json.loads(json_data)
            if 'fields' in data:
                fields = data['fields']
                print(f"   Fields in document:")
                for field_name in fields:
                    print(f"     - {field_name}")
        except:
            print("   Could not parse JSON data")
    
    # Check backup metadata
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='backup_metadata'")
    if cursor.fetchone():
        cursor.execute("SELECT * FROM backup_metadata")
        metadata = cursor.fetchall()
        print("\n" + "-" * 80)
        print("Backup metadata:")
        for row in metadata:
            print(f"  {row}")
    
    conn.close()
    print("\n" + "-" * 80)
    print("Inspection complete.")
    
except Exception as e:
    print(f"Error: {e}")