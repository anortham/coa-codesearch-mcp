// Test the Tree-sitter service with actual code
const { spawn } = require('child_process');
const path = require('path');

// Sample Go code to test
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
    // Process payment logic
    return true, nil
}`;

// Sample Python code to test
const pythonCode = `class UserService:
    """Service for managing users"""

    def __init__(self, db_connection):
        self.db = db_connection

    async def get_user(self, user_id: str) -> dict:
        """Get user by ID"""
        return await self.db.find_one({"id": user_id})

    @staticmethod
    def validate_email(email: str) -> bool:
        return "@" in email

class AdminUser(UserService):
    def __init__(self, db_connection, permissions):
        super().__init__(db_connection)
        self.permissions = permissions`;

// Start the service
const service = spawn('bun', ['src/index.ts'], {
  cwd: path.resolve(__dirname),
  stdio: ['pipe', 'pipe', 'pipe']
});

let outputBuffer = '';

service.stdout.on('data', (data) => {
  outputBuffer += data.toString();

  // Check if service is ready
  if (outputBuffer.includes('waiting for requests')) {
    console.log('Service started, sending test requests...\n');

    // Test Go extraction
    const goRequest = {
      action: 'extract',
      content: goCode,
      language: 'go',
      filePath: 'test.go'
    };

    service.stdin.write(JSON.stringify(goRequest) + '\n');

    // Test Python extraction after a delay
    setTimeout(() => {
      const pythonRequest = {
        action: 'extract',
        content: pythonCode,
        language: 'python',
        filePath: 'test.py'
      };

      service.stdin.write(JSON.stringify(pythonRequest) + '\n');

      // End the test after another delay
      setTimeout(() => {
        console.log('\nSending exit signal...');
        service.stdin.end();
      }, 1000);
    }, 1000);

    outputBuffer = ''; // Clear buffer to see only responses
  } else if (outputBuffer.includes('{') && outputBuffer.includes('}')) {
    // Try to parse JSON responses
    const lines = outputBuffer.split('\n');
    for (const line of lines) {
      if (line.trim() && line.includes('{')) {
        try {
          const response = JSON.parse(line);
          if (response.success) {
            console.log(`\n✅ ${response.language} extraction successful:`);
            console.log(`   Types found: ${response.types.length}`);
            response.types.forEach(t => {
              console.log(`     - ${t.kind} ${t.name} at line ${t.line}`);
            });
            console.log(`   Methods found: ${response.methods.length}`);
            response.methods.forEach(m => {
              console.log(`     - ${m.name}: ${m.signature}`);
            });
          } else {
            console.log(`\n❌ ${response.language} extraction failed:`, response.error);
          }
        } catch (e) {
          // Not a complete JSON yet
        }
      }
    }
  }
});

service.stderr.on('data', (data) => {
  const msg = data.toString();
  if (!msg.includes('waiting for requests') && !msg.includes('initialized successfully')) {
    console.error('Service error:', msg);
  }
});

service.on('close', (code) => {
  console.log(`\nService exited with code ${code}`);
  process.exit(code);
});