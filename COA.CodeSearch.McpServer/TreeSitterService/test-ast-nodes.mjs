import { spawn } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Start the Tree-sitter service with special debug mode
const service = spawn(path.join(__dirname, 'tree-sitter-service.exe'), [], {
  stdio: ['pipe', 'pipe', 'pipe']
});

// Collect all output
let output = '';
let errorOutput = '';

service.stdout.on('data', (data) => {
  output += data.toString();
});

service.stderr.on('data', (data) => {
  errorOutput += data.toString();
});

// Wait for service to be ready
setTimeout(() => {
  // Send extraction request for generator function
  const request = {
    action: 'extract',
    content: `function* fibonacci() {
    let [prev, curr] = [0, 1];
    while (true) {
        yield curr;
        [prev, curr] = [curr, prev + curr];
    }
}`,
    language: 'javascript',
    filePath: 'test.js'
  };

  service.stdin.write(JSON.stringify(request) + '\n');

  // Give it time to respond, then exit
  setTimeout(() => {
    service.kill();
    console.log('Response:', output.split('\n').filter(l => l.includes('"success"')).join('\n'));
    process.exit(0);
  }, 2000);
}, 1000);