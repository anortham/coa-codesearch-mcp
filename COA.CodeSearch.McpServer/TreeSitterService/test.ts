/**
 * Test script for Tree-sitter service
 */

console.log('Testing Tree-sitter service...\n');

// Test 1: Health check
console.log('Test 1: Health check');
const healthRequest = JSON.stringify({ action: 'health' });
console.log('Request:', healthRequest);

// Test 2: Python extraction
console.log('\nTest 2: Python code extraction');
const pythonCode = `
class UserService:
    def __init__(self, db_connection):
        self.db = db_connection

    async def get_user(self, user_id: int) -> dict:
        """Get user by ID"""
        return await self.db.fetch_one(f"SELECT * FROM users WHERE id = {user_id}")

    @staticmethod
    def validate_email(email: str) -> bool:
        return "@" in email
`;

const pythonRequest = JSON.stringify({
  action: 'extract',
  content: pythonCode,
  language: 'python',
  filePath: 'test.py'
});
console.log('Request:', pythonRequest);

// Test 3: Go extraction
console.log('\nTest 3: Go code extraction');
const goCode = `
package main

import "fmt"

type UserService struct {
    db Database
}

func (s *UserService) GetUser(id int) (*User, error) {
    user, err := s.db.FindUser(id)
    if err != nil {
        return nil, fmt.Errorf("failed to get user: %w", err)
    }
    return user, nil
}

func NewUserService(db Database) *UserService {
    return &UserService{db: db}
}
`;

const goRequest = JSON.stringify({
  action: 'extract',
  content: goCode,
  language: 'go',
  filePath: 'test.go'
});
console.log('Request:', goRequest);

// Test 4: C# extraction
console.log('\nTest 4: C# code extraction');
const csharpCode = `
namespace MyApp.Services
{
    public class UserService : IUserService
    {
        private readonly IDbConnection _db;

        public UserService(IDbConnection db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<User> GetUserAsync(int userId)
        {
            return await _db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id",
                new { Id = userId });
        }
    }
}
`;

const csharpRequest = JSON.stringify({
  action: 'extract',
  content: csharpCode,
  language: 'c-sharp',
  filePath: 'test.cs'
});
console.log('Request:', csharpRequest);

console.log('\nPaste these requests into the running service to test');