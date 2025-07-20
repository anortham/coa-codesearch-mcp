#!/usr/bin/env python3
import sqlite3
import re

db_path = r"C:\source\COA Roslyn MCP\.codesearch\memories.db"
guid_pattern = re.compile(r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')

print(f"Cleaning up invalid memories from: {db_path}")
print("-" * 80)

try:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    
    # First, back up the valid records
    cursor.execute("SELECT id FROM memories")
    all_ids = cursor.fetchall()
    
    invalid_count = 0
    for (id_val,) in all_ids:
        if not guid_pattern.match(id_val):
            invalid_count += 1
    
    print(f"Found {invalid_count} invalid records with file path IDs")
    
    if invalid_count > 0:
        response = input("Delete invalid records? (y/n): ")
        if response.lower() == 'y':
            # Delete records where ID is not a GUID
            cursor.execute("""
                DELETE FROM memories 
                WHERE id NOT LIKE '________-____-____-____-____________'
                OR id LIKE '%/%' OR id LIKE '%\\%'
            """)
            conn.commit()
            print(f"Deleted {cursor.rowcount} invalid records")
            
            # Show remaining records
            cursor.execute("SELECT scope, COUNT(*) FROM memories GROUP BY scope")
            remaining = cursor.fetchall()
            print("\nRemaining records by scope:")
            for scope, count in remaining:
                print(f"  {scope}: {count}")
    
    conn.close()
    print("\nCleanup complete.")
    
except Exception as e:
    print(f"Error: {e}")