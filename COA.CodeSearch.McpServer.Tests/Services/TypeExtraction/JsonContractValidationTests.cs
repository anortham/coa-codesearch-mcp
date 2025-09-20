using NUnit.Framework;
using FluentAssertions;
using System.Text.Json;
using COA.CodeSearch.McpServer.Services.TypeExtraction;
using System.Collections.Generic;

namespace COA.CodeSearch.McpServer.Tests.Services.TypeExtraction
{
    /// <summary>
    /// Validates that JSON contracts between C# and TypeScript Tree-sitter service remain compatible.
    /// These tests ensure that changes to either side don't break deserialization.
    /// </summary>
    [TestFixture]
    public class JsonContractValidationTests
    {
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        #region TypeExtractionResult Contract Tests

        [Test]
        public void TypeExtractionResult_DeserializeFromTypicalTreeSitterResponse_ShouldSucceed()
        {
            // Arrange - JSON response that matches TypeScript Tree-sitter service output
            var json = """
            {
              "success": true,
              "types": [
                {
                  "name": "UserService",
                  "kind": "class",
                  "signature": "public class UserService : IUserService",
                  "line": 5,
                  "column": 1,
                  "modifiers": ["public"],
                  "baseType": "IUserService",
                  "interfaces": ["IUserService"]
                }
              ],
              "methods": [
                {
                  "name": "GetUserById",
                  "signature": "public async Task<User> GetUserById(int id)",
                  "returnType": "Task<User>",
                  "line": 10,
                  "column": 5,
                  "containingType": "UserService",
                  "parameters": ["int id"],
                  "modifiers": ["public", "async"]
                }
              ],
              "language": "c-sharp"
            }
            """;

            // Act
            var result = JsonSerializer.Deserialize<TypeExtractionResult>(json, _jsonOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Success.Should().BeTrue();
            result.Language.Should().Be("c-sharp");

            result.Types.Should().HaveCount(1);
            var type = result.Types[0];
            type.Name.Should().Be("UserService");
            type.Kind.Should().Be("class");
            type.Signature.Should().Be("public class UserService : IUserService");
            type.Line.Should().Be(5);
            type.Column.Should().Be(1);
            type.Modifiers.Should().Contain("public");
            type.BaseType.Should().Be("IUserService");
            type.Interfaces.Should().Contain("IUserService");

            result.Methods.Should().HaveCount(1);
            var method = result.Methods[0];
            method.Name.Should().Be("GetUserById");
            method.Signature.Should().Be("public async Task<User> GetUserById(int id)");
            method.ReturnType.Should().Be("Task<User>");
            method.Line.Should().Be(10);
            method.Column.Should().Be(5);
            method.ContainingType.Should().Be("UserService");
            method.Parameters.Should().Contain("int id");
            method.Modifiers.Should().Contain("public").And.Contain("async");
        }

        [Test]
        public void TypeExtractionResult_DeserializeEmptyResponse_ShouldSucceed()
        {
            // Arrange - Minimal valid response
            var json = """
            {
              "success": true,
              "types": [],
              "methods": [],
              "language": "unknown"
            }
            """;

            // Act
            var result = JsonSerializer.Deserialize<TypeExtractionResult>(json, _jsonOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Success.Should().BeTrue();
            result.Types.Should().BeEmpty();
            result.Methods.Should().BeEmpty();
            result.Language.Should().Be("unknown");
        }

        [Test]
        public void TypeExtractionResult_DeserializeErrorResponse_ShouldSucceed()
        {
            // Arrange - Error response from Tree-sitter service
            var json = """
            {
              "success": false,
              "types": [],
              "methods": [],
              "language": "javascript",
              "error": "Parse error: unexpected token"
            }
            """;

            // Act
            var result = JsonSerializer.Deserialize<TypeExtractionResult>(json, _jsonOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Success.Should().BeFalse();
            result.Types.Should().BeEmpty();
            result.Methods.Should().BeEmpty();
            result.Language.Should().Be("javascript");
        }

        #endregion

        #region TypeInfo Contract Tests

        [Test]
        public void TypeInfo_DeserializeWithAllFields_ShouldSucceed()
        {
            // Arrange - Complete TypeInfo JSON
            var json = """
            {
              "name": "DatabaseContext",
              "kind": "class",
              "signature": "public class DatabaseContext : DbContext, IDisposable",
              "line": 15,
              "column": 1,
              "modifiers": ["public"],
              "baseType": "DbContext",
              "interfaces": ["IDisposable"]
            }
            """;

            // Act
            var typeInfo = JsonSerializer.Deserialize<TypeInfo>(json, _jsonOptions);

            // Assert
            typeInfo.Should().NotBeNull();
            typeInfo!.Name.Should().Be("DatabaseContext");
            typeInfo.Kind.Should().Be("class");
            typeInfo.Signature.Should().Be("public class DatabaseContext : DbContext, IDisposable");
            typeInfo.Line.Should().Be(15);
            typeInfo.Column.Should().Be(1);
            typeInfo.Modifiers.Should().Contain("public");
            typeInfo.BaseType.Should().Be("DbContext");
            typeInfo.Interfaces.Should().Contain("IDisposable");
        }

        [Test]
        public void TypeInfo_DeserializeMinimalFields_ShouldSucceed()
        {
            // Arrange - Minimal required fields only
            var json = """
            {
              "name": "SimpleClass",
              "kind": "class",
              "signature": "class SimpleClass",
              "line": 1,
              "column": 1
            }
            """;

            // Act
            var typeInfo = JsonSerializer.Deserialize<TypeInfo>(json, _jsonOptions);

            // Assert
            typeInfo.Should().NotBeNull();
            typeInfo!.Name.Should().Be("SimpleClass");
            typeInfo.Kind.Should().Be("class");
            typeInfo.Signature.Should().Be("class SimpleClass");
            typeInfo.Line.Should().Be(1);
            typeInfo.Column.Should().Be(1);
            typeInfo.Modifiers.Should().BeEmpty();
            typeInfo.BaseType.Should().BeNull();
            typeInfo.Interfaces.Should().BeNull();
        }

        [Test]
        public void TypeInfo_DeserializeInterface_ShouldSucceed()
        {
            // Arrange - Interface TypeInfo
            var json = """
            {
              "name": "IUserRepository",
              "kind": "interface",
              "signature": "public interface IUserRepository",
              "line": 8,
              "column": 1,
              "modifiers": ["public"]
            }
            """;

            // Act
            var typeInfo = JsonSerializer.Deserialize<TypeInfo>(json, _jsonOptions);

            // Assert
            typeInfo.Should().NotBeNull();
            typeInfo!.Name.Should().Be("IUserRepository");
            typeInfo.Kind.Should().Be("interface");
            typeInfo.Modifiers.Should().Contain("public");
        }

        #endregion

        #region MethodInfo Contract Tests

        [Test]
        public void MethodInfo_DeserializeWithAllFields_ShouldSucceed()
        {
            // Arrange - Complete MethodInfo JSON
            var json = """
            {
              "name": "ProcessPaymentAsync",
              "signature": "public async Task<PaymentResult> ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken)",
              "returnType": "Task<PaymentResult>",
              "line": 25,
              "column": 5,
              "containingType": "PaymentService",
              "parameters": ["Payment payment", "CancellationToken cancellationToken"],
              "modifiers": ["public", "async"]
            }
            """;

            // Act
            var methodInfo = JsonSerializer.Deserialize<MethodInfo>(json, _jsonOptions);

            // Assert
            methodInfo.Should().NotBeNull();
            methodInfo!.Name.Should().Be("ProcessPaymentAsync");
            methodInfo.Signature.Should().Be("public async Task<PaymentResult> ProcessPaymentAsync(Payment payment, CancellationToken cancellationToken)");
            methodInfo.ReturnType.Should().Be("Task<PaymentResult>");
            methodInfo.Line.Should().Be(25);
            methodInfo.Column.Should().Be(5);
            methodInfo.ContainingType.Should().Be("PaymentService");
            methodInfo.Parameters.Should().HaveCount(2);
            methodInfo.Parameters.Should().Contain("Payment payment").And.Contain("CancellationToken cancellationToken");
            methodInfo.Modifiers.Should().Contain("public").And.Contain("async");
        }

        [Test]
        public void MethodInfo_DeserializeMinimalFields_ShouldSucceed()
        {
            // Arrange - Minimal required fields
            var json = """
            {
              "name": "SimpleMethod",
              "signature": "void SimpleMethod()",
              "line": 5,
              "column": 3
            }
            """;

            // Act
            var methodInfo = JsonSerializer.Deserialize<MethodInfo>(json, _jsonOptions);

            // Assert
            methodInfo.Should().NotBeNull();
            methodInfo!.Name.Should().Be("SimpleMethod");
            methodInfo.Signature.Should().Be("void SimpleMethod()");
            methodInfo.ReturnType.Should().Be("void"); // Default value
            methodInfo.Line.Should().Be(5);
            methodInfo.Column.Should().Be(3);
            methodInfo.ContainingType.Should().BeNull();
            methodInfo.Parameters.Should().BeEmpty();
            methodInfo.Modifiers.Should().BeEmpty();
        }

        #endregion

        #region Case Sensitivity Tests

        [Test]
        public void JsonDeserialization_WithDifferentCasing_ShouldSucceed()
        {
            // Arrange - JSON with various casings (PascalCase, camelCase, lowercase)
            var json = """
            {
              "Success": true,
              "types": [
                {
                  "Name": "TestClass",
                  "kind": "class",
                  "SIGNATURE": "public class TestClass",
                  "line": 1,
                  "Column": 1
                }
              ],
              "Methods": [],
              "LANGUAGE": "c-sharp"
            }
            """;

            // Act
            var result = JsonSerializer.Deserialize<TypeExtractionResult>(json, _jsonOptions);

            // Assert - Should handle mixed casing gracefully
            result.Should().NotBeNull();
            result!.Success.Should().BeTrue();
            result.Types.Should().HaveCount(1);
            result.Types[0].Name.Should().Be("TestClass");
            result.Language.Should().Be("c-sharp");
        }

        #endregion

        #region Edge Cases and Error Handling

        [Test]
        public void JsonDeserialization_WithNullValues_ShouldHandleGracefully()
        {
            // Arrange - JSON with null values for optional fields
            // NOTE: This tests the actual behavior where JSON null becomes C# null
            var json = """
            {
              "success": true,
              "types": [
                {
                  "name": "TestClass",
                  "kind": "class",
                  "signature": "class TestClass",
                  "line": 1,
                  "column": 1,
                  "modifiers": null,
                  "baseType": null,
                  "interfaces": null
                }
              ],
              "methods": [],
              "language": "c-sharp"
            }
            """;

            // Act
            var result = JsonSerializer.Deserialize<TypeExtractionResult>(json, _jsonOptions);

            // Assert
            result.Should().NotBeNull();
            // IMPORTANT: JSON null deserializes to C# null, not empty list
            // TypeScript service should send [] instead of null for consistency
            result!.Types[0].Modifiers.Should().BeNull();
            result.Types[0].BaseType.Should().BeNull();
            result.Types[0].Interfaces.Should().BeNull();
        }

        [Test]
        public void JsonDeserialization_WithMissingOptionalFields_ShouldSucceed()
        {
            // Arrange - JSON missing optional fields
            var json = """
            {
              "success": true,
              "types": [
                {
                  "name": "MinimalClass",
                  "kind": "class",
                  "signature": "class MinimalClass",
                  "line": 1,
                  "column": 1
                }
              ],
              "methods": [],
              "language": "c-sharp"
            }
            """;

            // Act
            var result = JsonSerializer.Deserialize<TypeExtractionResult>(json, _jsonOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Types[0].Modifiers.Should().BeEmpty(); // Default empty list
            result.Types[0].BaseType.Should().BeNull();
            result.Types[0].Interfaces.Should().BeNull();
        }

        #endregion

        #region Compatibility Tests

        [Test]
        public void JsonContract_ShouldBeBackwardCompatible()
        {
            // Arrange - Old format that might be returned by legacy Tree-sitter responses
            var json = """
            {
              "success": true,
              "types": [],
              "methods": [
                {
                  "name": "OldMethod",
                  "signature": "void OldMethod()",
                  "line": 1,
                  "column": 1,
                  "parameters": ["string param"]
                }
              ],
              "language": "c-sharp"
            }
            """;

            // Act & Assert - Should not throw, should handle gracefully
            var act = () => JsonSerializer.Deserialize<TypeExtractionResult>(json, _jsonOptions);
            act.Should().NotThrow();

            var result = act();
            result.Should().NotBeNull();
            result!.Methods[0].Parameters.Should().Contain("string param");
        }

        [Test]
        public void JsonContract_WithExtraFields_ShouldIgnoreGracefully()
        {
            // Arrange - JSON with extra fields that don't exist in C# model
            var json = """
            {
              "success": true,
              "types": [
                {
                  "name": "TestClass",
                  "kind": "class",
                  "signature": "class TestClass",
                  "line": 1,
                  "column": 1,
                  "extraField": "should be ignored",
                  "anotherExtraField": 123
                }
              ],
              "methods": [],
              "language": "c-sharp",
              "extraTopLevelField": "ignored"
            }
            """;

            // Act & Assert - Should deserialize successfully, ignoring extra fields
            var result = JsonSerializer.Deserialize<TypeExtractionResult>(json, _jsonOptions);

            result.Should().NotBeNull();
            result!.Types[0].Name.Should().Be("TestClass");
            result.Language.Should().Be("c-sharp");
        }

        #endregion
    }
}