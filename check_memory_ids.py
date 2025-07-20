#!/usr/bin/env python3
import sqlite3
import json

# Connect to the backup database
conn = sqlite3.connect(r".codesearch\memories.db")
cursor = conn.cursor()

# Get all memory IDs from the backup
cursor.execute("SELECT id, scope FROM memories")
backup_ids = {}
for id_val, scope in cursor.fetchall():
    if id_val in backup_ids:
        print(f"DUPLICATE ID IN BACKUP: {id_val} (scopes: {backup_ids[id_val]}, {scope})")
    backup_ids[id_val] = scope

print(f"Total unique IDs in backup: {len(backup_ids)}")

# Now let's check for any patterns in the IDs
cursor.execute("SELECT id, scope, content FROM memories ORDER BY id")
all_memories = cursor.fetchall()

print("\nFirst 5 memory IDs:")
for id_val, scope, content in all_memories[:5]:
    print(f"  {id_val} ({scope}): {content[:50]}...")

conn.close()