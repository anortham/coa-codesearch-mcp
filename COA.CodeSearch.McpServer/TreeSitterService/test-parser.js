// Test script to understand web-tree-sitter exports
const TreeSitter = require('web-tree-sitter');

console.log('TreeSitter object:', TreeSitter);
console.log('TreeSitter type:', typeof TreeSitter);
console.log('TreeSitter properties:', Object.keys(TreeSitter));

// Check if Parser is a property
console.log('\nTreeSitter.Parser type:', typeof TreeSitter.Parser);
console.log('TreeSitter.Parser:', TreeSitter.Parser);

// Check for init function
console.log('\nTreeSitter.init exists?', typeof TreeSitter.init);

// Try to access Parser constructor
const Parser = TreeSitter.Parser;
console.log('\nIs Parser a constructor?', typeof Parser === 'function');

// Check Language
console.log('\nTreeSitter.Language:', TreeSitter.Language);

// Test if we need to initialize first
(async () => {
  try {
    // Try initializing TreeSitter
    if (typeof TreeSitter.init === 'function') {
      console.log('\nInitializing TreeSitter...');
      await TreeSitter.init();
      console.log('TreeSitter initialized!');
    }

    // Try creating a parser instance
    const parser = new Parser();
    console.log('Created parser instance:', parser);
    console.log('Parser instance methods:', Object.getOwnPropertyNames(Object.getPrototypeOf(parser)));
  } catch (error) {
    console.log('Error:', error.message);
  }
})();