import { spawn } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Start the Tree-sitter service
const service = spawn(path.join(__dirname, 'tree-sitter-service.exe'), [], {
  stdio: ['pipe', 'pipe', 'pipe']
});

// Handle stderr
service.stderr.on('data', (data) => {
  console.error(`STDERR: ${data}`);
});

// Handle stdout (responses)
service.stdout.on('data', (data) => {
  const lines = data.toString().split('\n').filter(line => line.trim());
  for (const line of lines) {
    try {
      const response = JSON.parse(line);
      console.log('RESPONSE:', JSON.stringify(response, null, 2));
    } catch (e) {
      // Not JSON, ignore
    }
  }
});

// Wait for service to be ready
setTimeout(() => {
  // Send extraction request for TypeScript interface with extends
  const request = {
    action: 'extract',
    content: `export interface AdminUser extends User {
  adminLevel: number;
  permissions: string[];
}`,
    language: 'typescript',
    filePath: 'test.ts'
  };

  service.stdin.write(JSON.stringify(request) + '\n');

  // Give it time to respond, then exit
  setTimeout(() => {
    service.kill();
    process.exit(0);
  }, 2000);
}, 1000);