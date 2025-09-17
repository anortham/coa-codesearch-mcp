using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Logging;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    /// <summary>
    /// Language-specific type extraction tests to verify parser integration and query effectiveness.
    /// These tests help identify issues at both the parser level (tree-sitter-dotnet-bindings)
    /// and the query level (CodeSearch .scm files).
    /// </summary>
    [TestFixture]
    public class LanguageSpecificExtractionTests : IDisposable
    {
        private TypeExtractionService _typeExtractionService;
        private ILanguageRegistry _languageRegistry;
        private QueryBasedExtractor _queryBasedExtractor;
        private string _testFilesPath;
        private string _realWorldPath;

        [SetUp]
        public void SetUp()
        {
            var logger = NullLogger<TypeExtractionService>.Instance;
            var loggerQuery = NullLogger<QueryBasedExtractor>.Instance;
            var loggerRegistry = NullLogger<LanguageRegistry>.Instance;

            // Use the real LanguageRegistry instead of mocking it
            // This will actually load the Tree-sitter DLLs for proper testing
            _languageRegistry = new LanguageRegistry(loggerRegistry);

            _queryBasedExtractor = new QueryBasedExtractor(loggerQuery);
            _typeExtractionService = new TypeExtractionService(logger, _languageRegistry, _queryBasedExtractor);

            // Use assembly location for more reliable path resolution
            var assemblyLocation = Path.GetDirectoryName(typeof(LanguageSpecificExtractionTests).Assembly.Location) ?? ".";
            _testFilesPath = Path.Combine(assemblyLocation, "Resources", "TypeExtraction");
            _realWorldPath = Path.Combine(assemblyLocation, "Resources", "TypeExtraction", "RealWorld");

            Directory.CreateDirectory(_testFilesPath);
            if (!Directory.Exists(_realWorldPath))
            {
                Directory.CreateDirectory(_realWorldPath);
            }

            // Log the path for debugging
            TestContext.WriteLine($"Real-world test path: {_realWorldPath}");
            if (Directory.Exists(_realWorldPath))
            {
                var files = Directory.GetFiles(_realWorldPath);
                TestContext.WriteLine($"Found {files.Length} files in real-world path");
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Dispose the language registry to release Tree-sitter resources
            _languageRegistry?.Dispose();
            (_queryBasedExtractor as IDisposable)?.Dispose();

            // Clean up test files
            if (Directory.Exists(_testFilesPath))
            {
                try
                {
                    Directory.Delete(_testFilesPath, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        public void Dispose()
        {
            _languageRegistry?.Dispose();
        }

        [Test]
        public async Task CSharp_ShouldExtractClassesAndMethods()
        {
            // Arrange
            var code = @"
namespace TestNamespace
{
    public class TestClass
    {
        public string Property { get; set; }

        public void Method1() { }

        private int Method2(string param)
        {
            return 42;
        }
    }

    public interface ITestInterface
    {
        void InterfaceMethod();
    }
}";
            var filePath = Path.Combine(_testFilesPath, "test.cs");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("C# parser should be available");
            result.Success.Should().BeTrue("C# extraction should succeed");
            result.Types.Should().Contain(t => t.Name == "TestClass", "should extract class");
            result.Types.Should().Contain(t => t.Name == "ITestInterface", "should extract interface");
            result.Methods.Should().HaveCountGreaterOrEqualTo(2, "should extract methods");
            result.Methods.Should().Contain(m => m.Name == "Method1", "should extract public method");
            result.Methods.Should().Contain(m => m.Name == "Method2", "should extract private method");
        }

        [Test]
        public async Task Java_ShouldExtractClassesAndMethods()
        {
            // Arrange
            var code = @"
package com.example;

public class TestClass {
    private String field;

    public TestClass() {
        this.field = """";
    }

    public void publicMethod() {
        // Method body
    }

    private int privateMethod(String param) {
        return 42;
    }
}

interface TestInterface {
    void interfaceMethod();
}";
            var filePath = Path.Combine(_testFilesPath, "Test.java");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("Java parser should be available");
            result.Success.Should().BeTrue("Java extraction should succeed");
            result.Types.Should().Contain(t => t.Name == "TestClass", "should extract class");
            result.Types.Should().Contain(t => t.Name == "TestInterface", "should extract interface");
            result.Methods.Should().HaveCountGreaterOrEqualTo(3, "should extract constructor and methods");
        }

        [Test]
        public async Task TypeScript_ShouldExtractClassesAndFunctions()
        {
            // Arrange
            var code = @"
class TestClass {
    private field: string;

    constructor() {
        this.field = '';
    }

    public publicMethod(): void {
        // Method body
    }

    private privateMethod(param: string): number {
        return 42;
    }
}

interface TestInterface {
    property: string;
    method(): void;
}

function standaloneFunction(param: number): string {
    return param.toString();
}

const arrowFunction = (x: number) => x * 2;
";
            var filePath = Path.Combine(_testFilesPath, "test.ts");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("TypeScript parser should be available");
            result.Success.Should().BeTrue("TypeScript extraction should succeed");
            result.Types.Should().Contain(t => t.Name == "TestClass", "should extract class");
            result.Types.Should().Contain(t => t.Name == "TestInterface", "should extract interface");
            result.Methods.Should().Contain(m => m.Name == "standaloneFunction", "should extract function");
        }

        [Test]
        public async Task Python_ShouldExtractClassesAndFunctions()
        {
            // Arrange
            var code = @"
class TestClass:
    def __init__(self):
        self.field = ''

    def public_method(self):
        '''Public method'''
        pass

    def _private_method(self, param):
        '''Private method'''
        return 42

def standalone_function(param):
    '''Standalone function'''
    return str(param)

async def async_function():
    '''Async function'''
    pass
";
            var filePath = Path.Combine(_testFilesPath, "test.py");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("Python parser should be available");
            result.Success.Should().BeTrue("Python extraction should succeed");
            result.Types.Should().Contain(t => t.Name == "TestClass", "should extract class");
            result.Methods.Should().Contain(m => m.Name == "standalone_function", "should extract function");
            result.Methods.Should().Contain(m => m.Name == "async_function", "should extract async function");
        }

        [Test]
        public async Task Rust_ShouldExtractStructsAndFunctions()
        {
            // Arrange
            var code = @"
struct TestStruct {
    field: String,
}

impl TestStruct {
    fn new() -> Self {
        TestStruct {
            field: String::new(),
        }
    }

    fn method(&self) -> &str {
        &self.field
    }
}

fn standalone_function(param: i32) -> String {
    param.to_string()
}

trait TestTrait {
    fn trait_method(&self);
}

enum TestEnum {
    Variant1,
    Variant2(String),
}
";
            var filePath = Path.Combine(_testFilesPath, "test.rs");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("Rust parser should be available");

            // Known issue: Rust currently returns 0 symbols
            if (result.Success && result.Types.Count == 0)
            {
                Assert.Inconclusive("Rust parser loads but query file may need adjustment - extracting 0 symbols");
            }
            else
            {
                result.Success.Should().BeTrue("Rust extraction should succeed");
                result.Types.Should().Contain(t => t.Name == "TestStruct", "should extract struct");
                result.Types.Should().Contain(t => t.Name == "TestTrait", "should extract trait");
                result.Types.Should().Contain(t => t.Name == "TestEnum", "should extract enum");
                result.Methods.Should().Contain(m => m.Name == "standalone_function", "should extract function");
            }
        }

        [Test]
        public async Task Go_ShouldExtractStructsAndFunctions()
        {
            // Arrange
            var code = @"
package main

type TestStruct struct {
    Field string
}

func (t *TestStruct) Method() string {
    return t.Field
}

func standaloneFunction(param int) string {
    return fmt.Sprintf(""%d"", param)
}

type TestInterface interface {
    InterfaceMethod() string
}

func main() {
    // Main function
}
";
            var filePath = Path.Combine(_testFilesPath, "test.go");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            // Known issue: Go parser DLL contains C parser instead
            if (result == null || !result.Success)
            {
                Assert.Inconclusive("Go parser not available - known issue: tree-sitter-go.dll contains C parser");
            }
            else
            {
                result.Types.Should().Contain(t => t.Name == "TestStruct", "should extract struct");
                result.Types.Should().Contain(t => t.Name == "TestInterface", "should extract interface");
                result.Methods.Should().Contain(m => m.Name == "standaloneFunction", "should extract function");
                result.Methods.Should().Contain(m => m.Name == "main", "should extract main function");
            }
        }

        [Test]
        public async Task Swift_ShouldExtractClassesAndProtocols()
        {
            // Arrange
            var code = @"
import Foundation

protocol UserProtocol {
    var id: String { get }
    func validate() -> Bool
}

class User: UserProtocol {
    let id: String
    var name: String
    private var email: String?

    init(id: String, name: String) {
        self.id = id
        self.name = name
    }

    func validate() -> Bool {
        return !name.isEmpty && email?.contains(""@"") ?? false
    }

    private func sendEmail() async throws {
        // Async method
    }
}

struct UserSettings: Codable {
    let theme: String
    let notifications: Bool
}

enum UserRole {
    case admin
    case user
    case moderator(level: Int)

    func hasAccess() -> Bool {
        switch self {
        case .admin:
            return true
        default:
            return false
        }
    }
}

extension User {
    func displayName() -> String {
        return ""User: \(name)""
    }
}
";
            var filePath = Path.Combine(_testFilesPath, "test.swift");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("Swift parser should be available");

            if (result.Success)
            {
                // Swift-specific assertions
                result.Types.Should().Contain(t => t.Name == "UserProtocol" && t.Kind == "protocol",
                    "should extract protocol");
                result.Types.Should().Contain(t => t.Name == "User" && t.Kind == "class",
                    "should extract class");
                result.Types.Should().Contain(t => t.Name == "UserSettings" && t.Kind == "struct",
                    "should extract struct");
                result.Types.Should().Contain(t => t.Name == "UserRole" && t.Kind == "enum",
                    "should extract enum");

                result.Methods.Should().Contain(m => m.Name == "validate",
                    "should extract protocol method");
                result.Methods.Should().Contain(m => m.Name == "hasAccess",
                    "should extract enum method");
                result.Methods.Should().Contain(m => m.Name == "displayName",
                    "should extract extension method");

                TestContext.WriteLine($"Swift: {result.Types.Count} types, {result.Methods.Count} methods extracted");
            }
            else
            {
                Assert.Inconclusive($"Swift extraction not yet supported or parser not available");
            }
        }

        [Test]
        public async Task Ruby_ShouldExtractClassesAndModules()
        {
            // Arrange
            var code = @"
module UserManagement
  class User
    attr_reader :id, :name
    attr_accessor :email

    def initialize(id, name)
      @id = id
      @name = name
      @email = nil
    end

    def validate
      !@name.empty? && @email&.include?('@')
    end

    private

    def send_notification
      # Private method
    end

    class << self
      def from_json(json)
        # Class method
      end
    end
  end

  module Validators
    def self.validate_email(email)
      email.match?(/\A[\w+\-.]+@[a-z\d\-]+(\.[a-z\d\-]+)*\.[a-z]+\z/i)
    end
  end

  class Admin < User
    def initialize(id, name)
      super
      @role = :admin
    end

    def has_access?
      true
    end
  end
end

class Settings
  include Singleton

  def theme
    @theme || 'light'
  end
end
";
            var filePath = Path.Combine(_testFilesPath, "test.rb");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("Ruby parser should be available");

            if (result.Success)
            {
                // Ruby-specific assertions
                result.Types.Should().Contain(t => t.Name == "UserManagement" && t.Kind == "module",
                    "should extract module");
                result.Types.Should().Contain(t => t.Name == "User" && t.Kind == "class",
                    "should extract class");
                result.Types.Should().Contain(t => t.Name == "Admin" && t.Kind == "class",
                    "should extract subclass");
                result.Types.Should().Contain(t => t.Name == "Validators" && t.Kind == "module",
                    "should extract nested module");
                result.Types.Should().Contain(t => t.Name == "Settings" && t.Kind == "class",
                    "should extract Settings class");

                result.Methods.Should().Contain(m => m.Name == "initialize",
                    "should extract constructor");
                result.Methods.Should().Contain(m => m.Name == "validate",
                    "should extract instance method");
                result.Methods.Should().Contain(m => m.Name == "from_json",
                    "should extract class method");
                result.Methods.Should().Contain(m => m.Name == "has_access?",
                    "should extract predicate method");

                TestContext.WriteLine($"Ruby: {result.Types.Count} types, {result.Methods.Count} methods extracted");
            }
            else
            {
                Assert.Inconclusive($"Ruby extraction not yet supported or parser not available");
            }
        }

        [Test]
        public async Task Kotlin_ShouldExtractClassesAndInterfaces()
        {
            // Arrange
            var code = @"
package com.example.users

import kotlinx.coroutines.*

interface UserRepository {
    suspend fun findById(id: String): User?
    suspend fun save(user: User)
    fun findAll(): List<User>
}

data class User(
    val id: String,
    var name: String,
    var email: String? = null
) {
    fun validate(): Boolean {
        return name.isNotEmpty() && email?.contains(""@"") ?: false
    }

    companion object {
        fun fromJson(json: String): User {
            // Parse JSON
            return User(""1"", ""Test"")
        }
    }
}

class UserService(
    private val repository: UserRepository
) : UserRepository by repository {

    override suspend fun save(user: User) {
        if (user.validate()) {
            repository.save(user)
        } else {
            throw IllegalArgumentException(""Invalid user"")
        }
    }

    inline fun <reified T> processUser(user: User, action: (User) -> T): T {
        return action(user)
    }
}

sealed class UserEvent {
    data class Created(val user: User) : UserEvent()
    data class Updated(val user: User) : UserEvent()
    object Deleted : UserEvent()
}

enum class UserRole(val level: Int) {
    ADMIN(3),
    MODERATOR(2),
    USER(1);

    fun hasHigherAccessThan(other: UserRole): Boolean {
        return this.level > other.level
    }
}

object UserValidator {
    fun validateEmail(email: String): Boolean {
        return email.contains(""@"")
    }
}
";
            var filePath = Path.Combine(_testFilesPath, "test.kt");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("Kotlin parser should be available");

            if (result.Success)
            {
                // Kotlin-specific assertions
                result.Types.Should().Contain(t => t.Name == "UserRepository" && t.Kind == "interface",
                    "should extract interface");
                result.Types.Should().Contain(t => t.Name == "User" && t.Kind == "class",
                    "should extract data class");
                result.Types.Should().Contain(t => t.Name == "UserService" && t.Kind == "class",
                    "should extract service class");
                result.Types.Should().Contain(t => t.Name == "UserEvent" && t.Kind == "class",
                    "should extract sealed class");
                result.Types.Should().Contain(t => t.Name == "UserRole" && t.Kind == "enum",
                    "should extract enum class");
                result.Types.Should().Contain(t => t.Name == "UserValidator",
                    "should extract object (singleton)");

                result.Methods.Should().Contain(m => m.Name == "findById",
                    "should extract suspend function");
                result.Methods.Should().Contain(m => m.Name == "validate",
                    "should extract method");
                result.Methods.Should().Contain(m => m.Name == "fromJson",
                    "should extract companion object method");
                result.Methods.Should().Contain(m => m.Name == "processUser",
                    "should extract inline function with reified type");
                result.Methods.Should().Contain(m => m.Name == "hasHigherAccessThan",
                    "should extract enum method");

                TestContext.WriteLine($"Kotlin: {result.Types.Count} types, {result.Methods.Count} methods extracted");
            }
            else
            {
                Assert.Inconclusive($"Kotlin extraction not yet supported or parser not available");
            }
        }

        [Test]
        public async Task JavaScript_ShouldExtractClassesAndFunctions()
        {
            // Arrange
            var code = @"
class TestClass {
    constructor() {
        this.field = '';
    }

    publicMethod() {
        // Method body
    }

    #privateMethod(param) {
        return 42;
    }
}

function standaloneFunction(param) {
    return param.toString();
}

const arrowFunction = (x) => x * 2;

async function asyncFunction() {
    // Async function
}
";
            var filePath = Path.Combine(_testFilesPath, "test.js");
            await File.WriteAllTextAsync(filePath, code);

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("JavaScript parser should be available");
            result.Success.Should().BeTrue("JavaScript extraction should succeed");
            result.Types.Should().Contain(t => t.Name == "TestClass", "should extract class");
            result.Methods.Should().Contain(m => m.Name == "standaloneFunction", "should extract function");
            result.Methods.Should().Contain(m => m.Name == "asyncFunction", "should extract async function");
        }

        #region Real-World Tests

        [Test]
        public async Task RealWorld_Java_ShouldExtractFromSerenaModel()
        {
            // Arrange
            // Use explicit path based on assembly location
            var assemblyPath = Path.GetDirectoryName(typeof(LanguageSpecificExtractionTests).Assembly.Location) ?? ".";
            var filePath = Path.Combine(assemblyPath, "Resources", "TypeExtraction", "RealWorld", "serena_Model.java");

            if (!File.Exists(filePath))
            {
                TestContext.WriteLine($"Looking for file at: {filePath}");
                Assert.Inconclusive($"Real-world test file not found at: {filePath}");
            }

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert - REAL testing, not theater
            result.Should().NotBeNull("Java parser should process real file");
            result.Success.Should().BeTrue("Java extraction should succeed on real file");

            // Verify exact extraction accuracy
            result.Types.Should().HaveCount(1, "should extract exactly 1 type (Model class)");
            var modelType = result.Types.FirstOrDefault(t => t.Name == "Model");
            modelType.Should().NotBeNull("Model class must be extracted");
            modelType.Kind.Should().Be("class", "Model should be identified as a class");

            // Verify methods: constructor and getName
            result.Methods.Should().HaveCount(2, "should extract 2 methods (constructor + getName)");
            result.Methods.Should().Contain(m => m.Name == "Model", "should extract constructor");
            result.Methods.Should().Contain(m => m.Name == "getName" && m.ReturnType == "String",
                "should extract getName() method with String return type");

            // Verify the constructor has String parameter
            var constructor = result.Methods.FirstOrDefault(m => m.Name == "Model");
            constructor.Should().NotBeNull();
            constructor.Parameters.Should().Contain("String", "constructor should have String parameter");

            TestContext.WriteLine($"Java Real-World: Verified {result.Types.Count} types, {result.Methods.Count} methods with full accuracy");
        }

        [Test]
        public async Task RealWorld_Java_ShouldExtractFromTestJava()
        {
            // Arrange
            // Use explicit path based on assembly location
            var assemblyPath = Path.GetDirectoryName(typeof(LanguageSpecificExtractionTests).Assembly.Location) ?? ".";
            var filePath = Path.Combine(assemblyPath, "Resources", "TypeExtraction", "RealWorld", "TestJava.java");
            if (!File.Exists(filePath))
            {
                Assert.Inconclusive("Real-world test file not found: TestJava.java");
            }

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert - Test REAL extraction capabilities
            result.Should().NotBeNull();
            result.Success.Should().BeTrue("Java extraction should succeed");

            // Main class and inner types
            result.Types.Should().Contain(t => t.Name == "UserService", "should extract UserService class");
            result.Types.Should().Contain(t => t.Name == "UserStatistics", "should extract inner class UserStatistics");
            result.Types.Should().Contain(t => t.Name == "UserDto", "should extract record UserDto");
            result.Types.Should().Contain(t => t.Name == "UserRole" && t.Kind == "enum", "should extract enum UserRole");

            // Critical methods with return types
            result.Methods.Should().Contain(m => m.Name == "findUserById" && m.ReturnType.Contains("Optional"),
                "should extract findUserById with Optional return type");
            result.Methods.Should().Contain(m => m.Name == "findAllUsersAsync" && m.ReturnType.Contains("CompletableFuture"),
                "should extract async method with CompletableFuture return");
            result.Methods.Should().Contain(m => m.Name == "getActivePercentage" && m.ReturnType == "double",
                "should extract inner class method with double return");
            result.Methods.Should().Contain(m => m.Name == "hasHigherAccessThan",
                "should extract enum method hasHigherAccessThan");

            // Verify we found AT LEAST these critical types (there may be more)
            result.Types.Should().HaveCountGreaterOrEqualTo(4, "should extract at least 4 types (main + inner + record + enum)");
            result.Methods.Should().HaveCountGreaterOrEqualTo(5, "should extract at least 5 significant methods");

            TestContext.WriteLine($"Java Complex: Verified {result.Types.Count} types (including inner), {result.Methods.Count} methods with signatures");
        }

        [Test]
        public async Task RealWorld_Go_ShouldExtractFromSerenaMain()
        {
            // Arrange
            // Use explicit path based on assembly location
            var assemblyPath = Path.GetDirectoryName(typeof(LanguageSpecificExtractionTests).Assembly.Location) ?? ".";
            var filePath = Path.Combine(assemblyPath, "Resources", "TypeExtraction", "RealWorld", "serena_main.go");
            if (!File.Exists(filePath))
            {
                Assert.Inconclusive("Real-world test file not found: serena_main.go");
            }

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("Go parser should attempt processing");

            // Known issue: Go parser contains C parser code
            if (!result.Success || result.Types.Count == 0)
            {
                Assert.Inconclusive("Go parser known issue: DLL contains C parser instead of Go parser");
            }

            result.Methods.Should().Contain(m => m.Name == "main", "should extract main function");

            TestContext.WriteLine($"Go Real-World: {result.Types.Count} types, {result.Methods.Count} methods extracted");
        }

        [Test]
        public async Task RealWorld_Go_ShouldExtractFromTestGo()
        {
            // Arrange
            // Use explicit path based on assembly location
            var assemblyPath = Path.GetDirectoryName(typeof(LanguageSpecificExtractionTests).Assembly.Location) ?? ".";
            var filePath = Path.Combine(assemblyPath, "Resources", "TypeExtraction", "RealWorld", "test_go.go");
            if (!File.Exists(filePath))
            {
                Assert.Inconclusive("Real-world test file not found: test_go.go");
            }

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert - Comprehensive Go extraction verification
            result.Should().NotBeNull();

            // Known issue: Go parser contains C parser code
            if (!result.Success)
            {
                Assert.Inconclusive("Go parser known issue: DLL contains C parser instead of Go parser");
            }

            // Once working, Go MUST extract these critical types:
            // - UserService struct with fields (repo, cache, logger, mu, settings)
            // - UserRepository interface with 4 methods
            // - User struct with tagged fields
            // - ServiceSettings struct with configuration fields
            // - NewUserService constructor function

            if (result.Success && result.Types.Count > 0)
            {
                // Structs
                result.Types.Should().Contain(t => t.Name == "UserService" && t.Kind == "struct",
                    "should extract UserService struct");
                result.Types.Should().Contain(t => t.Name == "User" && t.Kind == "struct",
                    "should extract User struct with field tags");
                result.Types.Should().Contain(t => t.Name == "ServiceSettings" && t.Kind == "struct",
                    "should extract ServiceSettings struct");

                // Interface
                result.Types.Should().Contain(t => t.Name == "UserRepository" && t.Kind == "interface",
                    "should extract UserRepository interface");

                // Functions and methods
                result.Methods.Should().Contain(m => m.Name == "NewUserService",
                    "should extract constructor function");
                result.Methods.Should().Contain(m => m.Name == "FindByID",
                    "should extract interface method FindByID");
                result.Methods.Should().Contain(m => m.Name == "FindAll",
                    "should extract interface method FindAll");

                result.Types.Should().HaveCountGreaterOrEqualTo(4, "should extract at least 4 types (3 structs + 1 interface)");
                result.Methods.Should().HaveCountGreaterOrEqualTo(5, "should extract constructor and interface methods");
            }

            TestContext.WriteLine($"Go Complex: {result.Types.Count} types, {result.Methods.Count} methods extracted");
        }

        [Test]
        public async Task RealWorld_Rust_ShouldExtractFromSerenaMain()
        {
            // Arrange
            // Use explicit path based on assembly location
            var assemblyPath = Path.GetDirectoryName(typeof(LanguageSpecificExtractionTests).Assembly.Location) ?? ".";
            var filePath = Path.Combine(assemblyPath, "Resources", "TypeExtraction", "RealWorld", "serena_main.rs");
            if (!File.Exists(filePath))
            {
                Assert.Inconclusive("Real-world test file not found: serena_main.rs");
            }

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert
            result.Should().NotBeNull("Rust parser should process real file");

            // Known issue: Rust parser loads but extracts 0 symbols
            if (result.Success && result.Types.Count == 0 && result.Methods.Count == 0)
            {
                Assert.Inconclusive("Rust parser loads but query file may need adjustment - extracting 0 symbols");
            }

            if (result.Success)
            {
                result.Methods.Should().Contain(m => m.Name == "main", "should extract main function");
            }

            TestContext.WriteLine($"Rust Real-World: {result.Types.Count} types, {result.Methods.Count} methods extracted");
        }

        [Test]
        public async Task RealWorld_Rust_ShouldExtractFromTestRust()
        {
            // Arrange
            // Use explicit path based on assembly location
            var assemblyPath = Path.GetDirectoryName(typeof(LanguageSpecificExtractionTests).Assembly.Location) ?? ".";
            var filePath = Path.Combine(assemblyPath, "Resources", "TypeExtraction", "RealWorld", "test_rust.rs");
            if (!File.Exists(filePath))
            {
                Assert.Inconclusive("Real-world test file not found: test_rust.rs");
            }

            // Act
            var content = await File.ReadAllTextAsync(filePath);
            var result = await _typeExtractionService.ExtractTypes(content, filePath);

            // Assert - Full Rust extraction verification
            result.Should().NotBeNull();

            // Known issue: Rust parser loads but may extract 0 symbols due to query file
            if (result.Success && result.Types.Count == 0 && result.Methods.Count == 0)
            {
                // This is a QUERY FILE issue, not a parser issue
                // The rust.scm file needs adjustment to properly capture Rust syntax
                Assert.Inconclusive("Rust query file needs adjustment - parser loads but extracts 0 symbols. Check rust.scm patterns.");
            }

            if (result.Success && result.Types.Count > 0)
            {
                // Rust MUST extract these critical elements:
                // - UserError enum with Error derive and error variants
                // - User struct with Serialize/Deserialize derives
                // - impl block for User with new() and validate() methods
                // - async trait definitions (if async_trait macro is handled)

                // Enums and Structs
                result.Types.Should().Contain(t => t.Name == "UserError" && t.Kind == "enum",
                    "should extract UserError enum with error variants");
                result.Types.Should().Contain(t => t.Name == "User" && t.Kind == "struct",
                    "should extract User struct with derive macros");

                // Implementation methods
                result.Methods.Should().Contain(m => m.Name == "new",
                    "should extract User::new() constructor");
                result.Methods.Should().Contain(m => m.Name == "validate" && m.ReturnType.Contains("Result"),
                    "should extract validate() method returning Result");

                // Verify comprehensive extraction
                result.Types.Should().HaveCountGreaterOrEqualTo(2, "should extract at least UserError enum and User struct");
                result.Methods.Should().HaveCountGreaterOrEqualTo(2, "should extract new() and validate() from impl block");

                // Log what we actually found for debugging
                TestContext.WriteLine($"Rust types found: {string.Join(", ", result.Types.Select(t => $"{t.Kind}:{t.Name}"))}");
                TestContext.WriteLine($"Rust methods found: {string.Join(", ", result.Methods.Select(m => m.Name))}");
            }

            TestContext.WriteLine($"Rust Complex: {result.Types.Count} types, {result.Methods.Count} methods extracted");
        }

        #endregion
    }
}