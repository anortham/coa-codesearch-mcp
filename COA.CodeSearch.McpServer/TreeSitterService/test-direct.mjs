// Direct test of type extraction
import { TreeSitterParser } from './src/parser.ts';

const parser = new TreeSitterParser();

// Sample Go code
const goCode = `package main

type User struct {
    Name string
    Age  int
}

func (u *User) String() string {
    return u.Name
}`;

// Sample Python code
const pythonCode = `class UserService:
    def __init__(self):
        self.users = []

    def get_user(self, id: int) -> dict:
        return self.users[id]`;

async function test() {
  try {
    console.log('Initializing parser...');
    await parser.initialize();

    console.log('\nTesting Go extraction...');
    const goResult = await parser.extractTypes(goCode, 'go', 'test.go');
    console.log('Go result:', JSON.stringify(goResult, null, 2));

    console.log('\nTesting Python extraction...');
    const pythonResult = await parser.extractTypes(pythonCode, 'python', 'test.py');
    console.log('Python result:', JSON.stringify(pythonResult, null, 2));

  } catch (error) {
    console.error('Test failed:', error);
  }
}

test();