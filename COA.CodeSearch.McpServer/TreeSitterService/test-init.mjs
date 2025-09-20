// Test module initialization with ES modules
import * as TreeSitter from 'web-tree-sitter';

console.log('TreeSitter module:', TreeSitter);
console.log('TreeSitter keys:', Object.keys(TreeSitter));
console.log('TreeSitter.Parser:', TreeSitter.Parser);

// Try to create a parser directly
try {
  console.log('\nTrying to create Parser directly...');
  const parser = new TreeSitter.Parser();
  console.log('Success! Created parser:', parser);
} catch (error) {
  console.log('Error creating Parser:', error.message);

  // Check if there's an init function we need to call
  console.log('\nChecking for init functions...');
  for (const key of Object.keys(TreeSitter)) {
    const value = TreeSitter[key];
    if (typeof value === 'function' && key.toLowerCase().includes('init')) {
      console.log(`Found potential init: ${key}`);
    }
  }
}