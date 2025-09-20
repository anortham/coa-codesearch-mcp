using NUnit.Framework;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using System.Threading.Tasks;
using System.Linq;
using Moq;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    [TestFixture]
    public class TypeScriptJavaScriptExtractorTests
    {
        private BunTreeSitterService _service = null!;
        private Mock<ILogger<BunTreeSitterService>> _loggerMock = null!;
        private IConfiguration _configuration = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _loggerMock = new Mock<ILogger<BunTreeSitterService>>();

            var configBuilder = new ConfigurationBuilder();
            var config = configBuilder.Build();

            var configSection = new Mock<IConfigurationSection>();
            configSection.Setup(x => x.Value).Returns((string?)null);

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(x => x.GetSection("CodeSearch:TreeSitterServicePath")).Returns(configSection.Object);

            _configuration = configMock.Object;
            _service = new BunTreeSitterService(_loggerMock.Object, _configuration);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _service?.Dispose();
        }

        #region TypeScript Tests

        [Test]
        public async Task ExtractTypes_TypeScript_Interface_ShouldExtractWithProperties()
        {
            // Arrange
            const string code = @"
export interface User {
    id: number;
    name: string;
    email: string;
    isActive: boolean;
    roles: string[];
    metadata?: Record<string, any>;
}

interface AdminUser extends User {
    adminLevel: number;
    permissions: Permission[];
}";

            // Act
            var result = await _service.ExtractTypesAsync(code, "typescript", "test.ts");

            // Assert
            result.Success.Should().BeTrue();
            result.Types.Should().HaveCount(2);

            var userInterface = result.Types.FirstOrDefault(t => t.Name == "User");
            userInterface.Should().NotBeNull();
            userInterface!.Kind.Should().Be("interface");
            userInterface.Line.Should().Be(2);

            var adminInterface = result.Types.FirstOrDefault(t => t.Name == "AdminUser");
            adminInterface.Should().NotBeNull();
            adminInterface!.Kind.Should().Be("interface");
            adminInterface.BaseTypes.Should().Contain("User");
        }

        [Test]
        public async Task ExtractTypes_TypeScript_Class_WithGenerics()
        {
            // Arrange
            const string code = @"
export class Repository<T extends BaseEntity> {
    private items: T[] = [];

    constructor(private readonly context: DbContext) {}

    async findById(id: string): Promise<T | null> {
        return this.items.find(item => item.id === id) || null;
    }

    async save(item: T): Promise<void> {
        this.items.push(item);
    }
}

class UserRepository extends Repository<User> {
    async findByEmail(email: string): Promise<User | null> {
        return null;
    }
}";

            // Act
            var result = await _service.ExtractTypesAsync(code, "typescript", "test.ts");

            // Assert
            result.Success.Should().BeTrue();
            result.Types.Should().HaveCount(2);

            var repository = result.Types.FirstOrDefault(t => t.Name == "Repository");
            repository.Should().NotBeNull();
            repository!.Kind.Should().Be("class");
            repository.TypeParameters.Should().Contain("T");

            result.Methods.Should().Contain(m => m.Name == "findById" && m.IsAsync);
            result.Methods.Should().Contain(m => m.Name == "save" && m.IsAsync);
            result.Methods.Should().Contain(m => m.Name == "findByEmail" && m.IsAsync);
        }

        [Test]
        public async Task ExtractTypes_TypeScript_TypeAlias_And_Enums()
        {
            // Arrange
            const string code = @"
export type UserId = string;
export type Result<T> = { success: true; data: T } | { success: false; error: string };

export enum UserRole {
    Admin = 'ADMIN',
    User = 'USER',
    Guest = 'GUEST'
}

const enum Status {
    Active = 1,
    Inactive = 0
}";

            // Act
            var result = await _service.ExtractTypesAsync(code, "typescript", "test.ts");

            // Assert
            result.Success.Should().BeTrue();
            result.Types.Should().Contain(t => t.Name == "UserId" && t.Kind == "type_alias");
            result.Types.Should().Contain(t => t.Name == "Result" && t.Kind == "type_alias");
            result.Types.Should().Contain(t => t.Name == "UserRole" && t.Kind == "enum");
            result.Types.Should().Contain(t => t.Name == "Status" && t.Kind == "enum");
        }

        [Test]
        public async Task ExtractMethods_TypeScript_AsyncArrowFunctions()
        {
            // Arrange
            const string code = @"
export const fetchUser = async (id: string): Promise<User> => {
    const response = await fetch(`/api/users/${id}`);
    return response.json();
};

const processUsers = async function(users: User[]): Promise<void> {
    for (const user of users) {
        await processUser(user);
    }
};

export async function* generateUsers(): AsyncGenerator<User> {
    let id = 0;
    while (true) {
        yield { id: id++, name: `User ${id}` };
    }
}";

            // Act
            var result = await _service.ExtractTypesAsync(code, "typescript", "test.ts");

            // Assert
            result.Success.Should().BeTrue();
            result.Methods.Should().Contain(m => m.Name == "fetchUser" && m.IsAsync);
            result.Methods.Should().Contain(m => m.Name == "processUsers" && m.IsAsync);
            result.Methods.Should().Contain(m => m.Name == "generateUsers" && m.IsAsync);

            var fetchUser = result.Methods.First(m => m.Name == "fetchUser");
            fetchUser.DetailedParameters.Should().HaveCount(1);
            fetchUser.DetailedParameters[0].Name.Should().Be("id");
            fetchUser.DetailedParameters[0].Type.Should().Be("string");
            fetchUser.ReturnType.Should().Be("Promise<User>");
        }

        [Test]
        public async Task ExtractTypes_TypeScript_Decorators()
        {
            // Arrange
            const string code = @"
@Controller('/users')
export class UserController {
    constructor(private userService: UserService) {}

    @Get('/:id')
    @Authorize('admin')
    async getUser(@Param('id') id: string): Promise<User> {
        return this.userService.findById(id);
    }

    @Post('/')
    @ValidateBody(UserSchema)
    async createUser(@Body() user: CreateUserDto): Promise<User> {
        return this.userService.create(user);
    }
}";

            // Act
            var result = await _service.ExtractTypesAsync(code, "typescript", "test.ts");

            // Assert
            result.Success.Should().BeTrue();

            var controller = result.Types.FirstOrDefault(t => t.Name == "UserController");
            controller.Should().NotBeNull();
            controller!.Modifiers.Should().Contain("@Controller");

            var getUser = result.Methods.FirstOrDefault(m => m.Name == "getUser");
            getUser.Should().NotBeNull();
            getUser!.Modifiers.Should().Contain("@Get");
            getUser.Modifiers.Should().Contain("@Authorize");
        }

        [Test]
        public async Task ExtractTypes_TypeScript_Namespaces_And_Modules()
        {
            // Arrange
            const string code = @"
export namespace API {
    export interface Request {
        headers: Record<string, string>;
        body: unknown;
    }

    export interface Response {
        status: number;
        data: unknown;
    }

    export class Client {
        async request(req: Request): Promise<Response> {
            return { status: 200, data: null };
        }
    }
}

module Utils {
    export function formatDate(date: Date): string {
        return date.toISOString();
    }
}";

            // Act
            var result = await _service.ExtractTypesAsync(code, "typescript", "test.ts");

            // Assert
            result.Success.Should().BeTrue();
            result.Types.Should().Contain(t => t.Name == "API" && t.Kind == "namespace");
            result.Types.Should().Contain(t => t.Name == "Request" && t.Namespace == "API");
            result.Types.Should().Contain(t => t.Name == "Response" && t.Namespace == "API");
            result.Types.Should().Contain(t => t.Name == "Client" && t.Namespace == "API");
            result.Types.Should().Contain(t => t.Name == "Utils" && t.Kind == "module");
        }

        #endregion

        #region JavaScript Tests

        [Test]
        public async Task ExtractTypes_JavaScript_Classes_ES6()
        {
            // Arrange
            const string code = @"
export class Animal {
    constructor(name) {
        this.name = name;
    }

    speak() {
        console.log(`${this.name} makes a sound`);
    }

    static createDog(name) {
        return new Dog(name);
    }
}

class Dog extends Animal {
    constructor(name, breed) {
        super(name);
        this.breed = breed;
    }

    speak() {
        console.log(`${this.name} barks`);
    }

    async fetch() {
        await delay(1000);
        return 'ball';
    }
}";

            // Act
            var result = await _service.ExtractTypesAsync(code, "javascript", "test.js");

            // Assert
            result.Success.Should().BeTrue();
            result.Types.Should().HaveCount(2);

            var animal = result.Types.FirstOrDefault(t => t.Name == "Animal");
            animal.Should().NotBeNull();
            animal!.Kind.Should().Be("class");

            var dog = result.Types.FirstOrDefault(t => t.Name == "Dog");
            dog.Should().NotBeNull();
            dog!.BaseTypes.Should().Contain("Animal");

            result.Methods.Should().Contain(m => m.Name == "speak");
            result.Methods.Should().Contain(m => m.Name == "createDog" && m.IsStatic);
            result.Methods.Should().Contain(m => m.Name == "fetch" && m.IsAsync);
        }

        [Test]
        public async Task ExtractMethods_JavaScript_FunctionDeclarations()
        {
            // Arrange
            const string code = @"
// Regular function
function calculateSum(a, b) {
    return a + b;
}

// Arrow function
const multiply = (x, y) => x * y;

// Async function
async function fetchData(url) {
    const response = await fetch(url);
    return response.json();
}

// Generator function
function* fibonacci() {
    let [prev, curr] = [0, 1];
    while (true) {
        yield curr;
        [prev, curr] = [curr, prev + curr];
    }
}

// Function with default parameters and rest
function processItems(first, second = 10, ...rest) {
    return [first, second, ...rest];
}

// Immediately Invoked Function Expression (IIFE)
(function initApp() {
    console.log('App initialized');
})();";

            // Act
            var result = await _service.ExtractTypesAsync(code, "javascript", "test.js");

            // Assert
            result.Success.Should().BeTrue();
            result.Methods.Should().Contain(m => m.Name == "calculateSum");
            result.Methods.Should().Contain(m => m.Name == "multiply");
            result.Methods.Should().Contain(m => m.Name == "fetchData" && m.IsAsync);
            result.Methods.Should().Contain(m => m.Name == "fibonacci" && m.IsGenerator);
            result.Methods.Should().Contain(m => m.Name == "processItems");
            result.Methods.Should().Contain(m => m.Name == "initApp");

            var processItems = result.Methods.First(m => m.Name == "processItems");
            processItems.DetailedParameters.Should().HaveCount(3);
            processItems.DetailedParameters[1].HasDefaultValue.Should().BeTrue();
            processItems.DetailedParameters[2].IsRestParameter.Should().BeTrue();
        }

        [Test]
        public async Task ExtractTypes_JavaScript_Prototypes()
        {
            // Arrange
            const string code = @"
function Person(name, age) {
    this.name = name;
    this.age = age;
}

Person.prototype.greet = function() {
    return `Hello, I'm ${this.name}`;
};

Person.prototype.getAge = function() {
    return this.age;
};

Person.create = function(name, age) {
    return new Person(name, age);
};

// Object with methods
const UserService = {
    users: [],

    addUser(user) {
        this.users.push(user);
    },

    async findUser(id) {
        await this.loadUsers();
        return this.users.find(u => u.id === id);
    },

    get userCount() {
        return this.users.length;
    }
};";

            // Act
            var result = await _service.ExtractTypesAsync(code, "javascript", "test.js");

            // Assert
            result.Success.Should().BeTrue();

            result.Types.Should().Contain(t => t.Name == "Person" && t.Kind == "constructor_function");
            result.Types.Should().Contain(t => t.Name == "UserService" && t.Kind == "object_literal");

            result.Methods.Should().Contain(m => m.Name == "greet" && m.ClassName == "Person");
            result.Methods.Should().Contain(m => m.Name == "getAge" && m.ClassName == "Person");
            result.Methods.Should().Contain(m => m.Name == "create" && m.IsStatic && m.ClassName == "Person");
            result.Methods.Should().Contain(m => m.Name == "addUser" && m.ClassName == "UserService");
            result.Methods.Should().Contain(m => m.Name == "findUser" && m.IsAsync && m.ClassName == "UserService");
        }

        [Test]
        public async Task ExtractTypes_JavaScript_ModulePatterns()
        {
            // Arrange
            const string code = @"
// CommonJS exports
module.exports = {
    processData: function(data) {
        return data;
    }
};

module.exports.helperFunction = function() {
    return 'helper';
};

// ES6 exports
export default class DefaultExport {
    method() {}
}

export const namedExport = () => {};
export function anotherExport() {}
export { localFunction as exportedFunction };

// Mixed exports
const api = {
    get: async (url) => fetch(url),
    post: async (url, data) => fetch(url, { method: 'POST', body: data })
};

export default api;
export const { get, post } = api;";

            // Act
            var result = await _service.ExtractTypesAsync(code, "javascript", "test.js");

            // Assert
            result.Success.Should().BeTrue();

            result.Types.Should().Contain(t => t.Name == "DefaultExport" && t.IsExported);
            result.Methods.Should().Contain(m => m.Name == "processData");
            result.Methods.Should().Contain(m => m.Name == "helperFunction");
            result.Methods.Should().Contain(m => m.Name == "namedExport" && m.IsExported);
            result.Methods.Should().Contain(m => m.Name == "anotherExport" && m.IsExported);
            result.Methods.Should().Contain(m => m.Name == "get" && m.IsAsync);
            result.Methods.Should().Contain(m => m.Name == "post" && m.IsAsync);
        }

        [Test]
        public async Task ExtractTypes_JavaScript_JSDoc_TypeAnnotations()
        {
            // Arrange
            const string code = @"
/**
 * Represents a user in the system
 * @class
 */
class User {
    /**
     * @param {string} name - The user's name
     * @param {number} age - The user's age
     * @param {string[]} roles - User roles
     */
    constructor(name, age, roles) {
        this.name = name;
        this.age = age;
        this.roles = roles;
    }
}

/**
 * @typedef {Object} Product
 * @property {string} id - Product ID
 * @property {string} name - Product name
 * @property {number} price - Product price
 */

/**
 * Process order items
 * @param {Product[]} items - Array of products
 * @returns {Promise<number>} Total price
 * @async
 */
async function processOrder(items) {
    return items.reduce((sum, item) => sum + item.price, 0);
}

/**
 * @interface
 */
function IRepository() {}
IRepository.prototype.find = function(id) {};
IRepository.prototype.save = function(item) {};";

            // Act
            var result = await _service.ExtractTypesAsync(code, "javascript", "test.js");

            // Assert
            result.Success.Should().BeTrue();

            var user = result.Types.FirstOrDefault(t => t.Name == "User");
            user.Should().NotBeNull();

            result.Types.Should().Contain(t => t.Name == "Product" && t.Kind == "typedef");
            result.Types.Should().Contain(t => t.Name == "IRepository" && t.Kind == "interface");

            var processOrderMethod = result.Methods.FirstOrDefault(m => m.Name == "processOrder");
            processOrderMethod.Should().NotBeNull();
            processOrderMethod!.IsAsync.Should().BeTrue();
            processOrderMethod.ReturnType.Should().Be("Promise<number>");
            processOrderMethod.DetailedParameters.Should().HaveCount(1);
            processOrderMethod.DetailedParameters[0].Type.Should().Be("Product[]");
        }

        #endregion

        #region React/JSX Tests

        [Test]
        public async Task ExtractTypes_React_FunctionalComponents()
        {
            // Arrange
            const string code = @"
import React, { useState, useEffect } from 'react';

export const UserCard = ({ user, onEdit }) => {
    const [isEditing, setIsEditing] = useState(false);

    return (
        <div className='user-card'>
            <h2>{user.name}</h2>
            <button onClick={() => onEdit(user)}>Edit</button>
        </div>
    );
};

export default function UserList({ users }) {
    useEffect(() => {
        console.log('Users loaded');
    }, [users]);

    return (
        <div>
            {users.map(user => <UserCard key={user.id} user={user} />)}
        </div>
    );
}

const MemoizedComponent = React.memo(({ data }) => {
    return <div>{data}</div>;
});

export const ForwardedInput = React.forwardRef((props, ref) => {
    return <input ref={ref} {...props} />;
});";

            // Act
            var result = await _service.ExtractTypesAsync(code, "javascript", "test.jsx");

            // Assert
            result.Success.Should().BeTrue();
            result.Types.Should().Contain(t => t.Name == "UserCard" && t.Kind == "react_component");
            result.Types.Should().Contain(t => t.Name == "UserList" && t.Kind == "react_component");
            result.Types.Should().Contain(t => t.Name == "MemoizedComponent" && t.Kind == "react_component");
            result.Types.Should().Contain(t => t.Name == "ForwardedInput" && t.Kind == "react_component");
        }

        #endregion
    }
}