// Comprehensive test of type extraction
import { TreeSitterParser } from './src/parser.ts';

const parser = new TreeSitterParser();

// Sample Go code with various constructs
const goCode = `package main

import "fmt"

type User struct {
    Name string
    Age  int
}

type UserService interface {
    GetUser(id string) (*User, error)
    CreateUser(user User) error
}

func (u *User) String() string {
    return fmt.Sprintf("%s (%d)", u.Name, u.Age)
}

func ProcessPayment(amount float64, currency string) (bool, error) {
    return true, nil
}

func main() {
    user := &User{Name: "John", Age: 30}
    fmt.Println(user.String())
}`;

// Sample Python code with various constructs
const pythonCode = `from typing import List, Optional
import asyncio

class UserService:
    """Service for managing users"""

    def __init__(self, db_connection):
        self.db = db_connection
        self._cache = {}

    async def get_user(self, user_id: str) -> Optional[dict]:
        """Get user by ID"""
        if user_id in self._cache:
            return self._cache[user_id]
        return await self.db.find_one({"id": user_id})

    @staticmethod
    def validate_email(email: str) -> bool:
        """Validate email format"""
        return "@" in email and "." in email

    @classmethod
    def from_config(cls, config: dict):
        """Create service from configuration"""
        return cls(config.get("db_connection"))

class AdminUser(UserService):
    """Admin user with special permissions"""

    def __init__(self, db_connection, permissions: List[str]):
        super().__init__(db_connection)
        self.permissions = permissions

    async def delete_user(self, user_id: str) -> bool:
        """Delete a user (admin only)"""
        return await self.db.delete_one({"id": user_id})

def process_users(users: List[dict]) -> int:
    """Process a list of users"""
    return len([u for u in users if u.get("active")])

async def main():
    service = UserService(None)
    user = await service.get_user("123")
    print(user)`;

// Sample C# code
const csharpCode = `using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UserManagement
{
    public interface IUserService
    {
        Task<User> GetUserAsync(string id);
        Task<bool> CreateUserAsync(User user);
    }

    public class User
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }

        public User(string name, int age)
        {
            Name = name;
            Age = age;
            Id = Guid.NewGuid().ToString();
        }

        public override string ToString()
        {
            return $"{Name} ({Age})";
        }
    }

    public class UserService : IUserService
    {
        private readonly IDatabase _database;

        public UserService(IDatabase database)
        {
            _database = database;
        }

        public async Task<User> GetUserAsync(string id)
        {
            return await _database.FindAsync<User>(id);
        }

        public async Task<bool> CreateUserAsync(User user)
        {
            return await _database.InsertAsync(user);
        }

        protected virtual void OnUserCreated(User user)
        {
            // Event handling
        }
    }

    public static class UserExtensions
    {
        public static bool IsAdult(this User user)
        {
            return user.Age >= 18;
        }
    }
}`;

async function test() {
  try {
    console.log('Initializing parser...');
    await parser.initialize();

    console.log('\n========== GO EXTRACTION ==========');
    const goResult = await parser.extractTypes(goCode, 'go', 'test.go');
    console.log(`✅ Success: ${goResult.success}`);
    console.log(`📦 Types found: ${goResult.types.length}`);
    goResult.types.forEach(t => {
      console.log(`   - ${t.kind} ${t.name} (line ${t.line})`);
    });
    console.log(`🔧 Methods found: ${goResult.methods.length}`);
    goResult.methods.forEach(m => {
      console.log(`   - ${m.name}: ${m.signature}`);
    });

    console.log('\n========== PYTHON EXTRACTION ==========');
    const pythonResult = await parser.extractTypes(pythonCode, 'python', 'test.py');
    console.log(`✅ Success: ${pythonResult.success}`);
    console.log(`📦 Types found: ${pythonResult.types.length}`);
    pythonResult.types.forEach(t => {
      console.log(`   - ${t.kind} ${t.name} (line ${t.line})`);
      if (t.baseTypes?.length) {
        console.log(`     extends: ${t.baseTypes.join(', ')}`);
      }
    });
    console.log(`🔧 Methods found: ${pythonResult.methods.length}`);
    pythonResult.methods.forEach(m => {
      console.log(`   - ${m.name}: ${m.signature}`);
      if (m.modifiers?.length) {
        console.log(`     modifiers: ${m.modifiers.join(', ')}`);
      }
    });

    console.log('\n========== C# EXTRACTION ==========');
    const csharpResult = await parser.extractTypes(csharpCode, 'c-sharp', 'test.cs');
    console.log(`✅ Success: ${csharpResult.success}`);
    console.log(`📦 Types found: ${csharpResult.types.length}`);
    csharpResult.types.forEach(t => {
      console.log(`   - ${t.kind} ${t.name} (line ${t.line})`);
      if (t.baseTypes?.length) {
        console.log(`     implements/extends: ${t.baseTypes.join(', ')}`);
      }
      if (t.modifiers?.length) {
        console.log(`     modifiers: ${t.modifiers.join(', ')}`);
      }
    });
    console.log(`🔧 Methods found: ${csharpResult.methods.length}`);
    csharpResult.methods.forEach(m => {
      console.log(`   - ${m.name}: ${m.signature}`);
      if (m.modifiers?.length) {
        console.log(`     modifiers: ${m.modifiers.join(', ')}`);
      }
    });

    console.log('\n========== SUMMARY ==========');
    console.log(`Go: ${goResult.types.length} types, ${goResult.methods.length} methods`);
    console.log(`Python: ${pythonResult.types.length} types, ${pythonResult.methods.length} methods`);
    console.log(`C#: ${csharpResult.types.length} types, ${csharpResult.methods.length} methods`);

  } catch (error) {
    console.error('Test failed:', error);
  }
}

test();