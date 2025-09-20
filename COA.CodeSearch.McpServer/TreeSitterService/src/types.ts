/**
 * Type definitions matching the C# TypeExtractionResult models
 */

export interface ExtractedType {
  name: string;
  kind: string; // More flexible for different language constructs
  signature: string;  // Full type signature (e.g., "public class UserService : IUserService")
  line: number;
  column: number;
  endLine?: number;
  endColumn?: number;
  modifiers?: string[];
  baseType?: string; // Legacy single base type
  baseTypes?: string[]; // Multiple base types/interfaces
  interfaces?: string[]; // Explicit interfaces
  typeParameters?: string[];
  namespace?: string;
  isExported?: boolean;
}

export interface ExtractedMethod {
  name: string;
  signature: string;
  line: number;
  column: number;
  endLine?: number;
  endColumn?: number;
  modifiers?: string[];
  returnType?: string;
  parameters?: string[]; // Legacy simple parameter list
  detailedParameters?: MethodParameter[];
  isAsync?: boolean;
  isConstructor?: boolean;
  isStatic?: boolean;
  isGenerator?: boolean;
  isExported?: boolean;
  containingType?: string;
  className?: string;
}

export interface MethodParameter {
  name: string;
  type?: string;
  hasDefaultValue?: boolean;
  defaultValue?: string;
  isRestParameter?: boolean;
  isOptional?: boolean;
}

export interface TypeExtractionResult {
  success: boolean;
  types: ExtractedType[];
  methods: ExtractedMethod[];
  language: string;
  error?: string;
}

export interface ExtractionRequest {
  action: 'extract' | 'health' | 'supported-languages';
  content?: string;
  language?: string;
  filePath?: string;
}

export interface HealthResponse {
  status: 'healthy';
  version: string;
  supportedLanguages: string[];
}

export interface ErrorResponse {
  success: false;
  error: string;
}

export type ServiceResponse = TypeExtractionResult | HealthResponse | ErrorResponse;