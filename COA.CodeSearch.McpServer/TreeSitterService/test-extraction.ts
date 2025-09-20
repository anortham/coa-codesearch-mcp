/**
 * Standalone test script for Tree-sitter type extraction
 * Run with: bun run test-extraction.ts
 */

import { TreeSitterParser } from './src/parser';
import * as fs from 'fs';
import * as path from 'path';

// Test code samples
const goCode = `
package userservice

type UserService struct {
    repo     UserRepository
    cache    Cache
    logger   Logger
}

type UserRepository interface {
    FindByID(ctx context.Context, id string) (*User, error)
    Save(ctx context.Context, user *User) error
}

func (s *UserService) GetUser(id string) (*User, error) {
    return s.repo.FindByID(context.Background(), id)
}
`;

const pythonCode = `
class UserService:
    def __init__(self, repo, cache):
        self.repo = repo
        self.cache = cache

    def get_user(self, user_id: str) -> User:
        return self.repo.find_by_id(user_id)

class UserRepository:
    def find_by_id(self, user_id: str) -> User:
        pass
`;

const csharpCode = `
public class UserService : IUserService
{
    private readonly IUserRepository _repo;

    public UserService(IUserRepository repo)
    {
        _repo = repo;
    }

    public async Task<User> GetUserAsync(string id)
    {
        return await _repo.FindByIdAsync(id);
    }
}

public interface IUserRepository
{
    Task<User> FindByIdAsync(string id);
}
`;

async function testExtraction() {
    console.log('=== Tree-sitter Type Extraction Test ===\n');
    console.log(`Version: 1.0.0-test`);
    console.log(`Timestamp: ${new Date().toISOString()}\n`);

    const parser = new TreeSitterParser();

    try {
        await parser.initialize();
        console.log('✓ Parser initialized\n');

        // Test Go extraction
        console.log('--- Testing Go ---');
        const goResult = await parser.extractTypes(goCode, 'go', 'test.go');
        console.log('Go Result:', JSON.stringify(goResult, null, 2));
        console.log(`Types found: ${goResult.types?.length || 0}`);
        console.log(`Methods found: ${goResult.methods?.length || 0}`);

        // Check for signature field
        if (goResult.types && goResult.types.length > 0) {
            const firstType = goResult.types[0];
            console.log(`First type has signature: ${firstType.signature ? '✓' : '✗ MISSING'}`);
        }
        console.log();

        // Test Python extraction
        console.log('--- Testing Python ---');
        const pythonResult = await parser.extractTypes(pythonCode, 'python', 'test.py');
        console.log('Python Result:', JSON.stringify(pythonResult, null, 2));
        console.log(`Types found: ${pythonResult.types?.length || 0}`);
        console.log(`Methods found: ${pythonResult.methods?.length || 0}`);

        if (pythonResult.types && pythonResult.types.length > 0) {
            const firstType = pythonResult.types[0];
            console.log(`First type has signature: ${firstType.signature ? '✓' : '✗ MISSING'}`);
        }
        console.log();

        // Test C# extraction
        console.log('--- Testing C# ---');
        const csharpResult = await parser.extractTypes(csharpCode, 'c-sharp', 'test.cs');
        console.log('C# Result:', JSON.stringify(csharpResult, null, 2));
        console.log(`Types found: ${csharpResult.types?.length || 0}`);
        console.log(`Methods found: ${csharpResult.methods?.length || 0}`);

        if (csharpResult.types && csharpResult.types.length > 0) {
            const firstType = csharpResult.types[0];
            console.log(`First type has signature: ${firstType.signature ? '✓' : '✗ MISSING'}`);
        }

    } catch (error) {
        console.error('Error during extraction:', error);
    }
}

// Run the test
testExtraction();