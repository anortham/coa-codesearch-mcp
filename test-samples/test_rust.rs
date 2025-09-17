use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use async_trait::async_trait;
use serde::{Deserialize, Serialize};
use thiserror::Error;

/// Error types for the user service
#[derive(Error, Debug)]
pub enum UserError {
    #[error("User not found: {id}")]
    NotFound { id: u64 },

    #[error("Database error: {0}")]
    DatabaseError(#[from] sqlx::Error),

    #[error("Invalid user data: {reason}")]
    ValidationError { reason: String },
}

/// User entity with serialization support
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct User {
    pub id: u64,
    pub name: String,
    pub email: String,
    pub active: bool,
    pub created_at: chrono::DateTime<chrono::Utc>,
}

impl User {
    /// Creates a new user instance
    pub fn new(name: String, email: String) -> Self {
        Self {
            id: 0,
            name,
            email,
            active: true,
            created_at: chrono::Utc::now(),
        }
    }

    /// Validates user data
    pub fn validate(&self) -> Result<(), UserError> {
        if self.name.is_empty() {
            return Err(UserError::ValidationError {
                reason: "Name cannot be empty".to_string(),
            });
        }
        if !self.email.contains('@') {
            return Err(UserError::ValidationError {
                reason: "Invalid email format".to_string(),
            });
        }
        Ok(())
    }
}

/// Repository trait for user data access
#[async_trait]
pub trait UserRepository: Send + Sync {
    async fn find_by_id(&self, id: u64) -> Result<Option<User>, UserError>;
    async fn find_all(&self) -> Result<Vec<User>, UserError>;
    async fn save(&self, user: &User) -> Result<u64, UserError>;
    async fn delete(&self, id: u64) -> Result<(), UserError>;
}

/// User service implementation
pub struct UserService<R: UserRepository> {
    repository: Arc<R>,
    cache: Arc<Mutex<HashMap<u64, User>>>,
    config: ServiceConfig,
}

/// Service configuration
#[derive(Clone)]
pub struct ServiceConfig {
    pub cache_enabled: bool,
    pub max_cache_size: usize,
    pub timeout_secs: u64,
}

impl Default for ServiceConfig {
    fn default() -> Self {
        Self {
            cache_enabled: true,
            max_cache_size: 1000,
            timeout_secs: 30,
        }
    }
}

impl<R: UserRepository> UserService<R> {
    /// Creates a new service instance
    pub fn new(repository: R) -> Self {
        Self {
            repository: Arc::new(repository),
            cache: Arc::new(Mutex::new(HashMap::new())),
            config: ServiceConfig::default(),
        }
    }

    /// Gets a user by ID with caching
    pub async fn get_user(&self, id: u64) -> Result<User, UserError> {
        // Check cache first
        if self.config.cache_enabled {
            if let Ok(cache) = self.cache.lock() {
                if let Some(user) = cache.get(&id) {
                    return Ok(user.clone());
                }
            }
        }

        // Fetch from repository
        let user = self.repository
            .find_by_id(id)
            .await?
            .ok_or(UserError::NotFound { id })?;

        // Update cache
        if self.config.cache_enabled {
            if let Ok(mut cache) = self.cache.lock() {
                cache.insert(id, user.clone());
            }
        }

        Ok(user)
    }

    /// Creates a new user
    pub async fn create_user(&self, name: String, email: String) -> Result<u64, UserError> {
        let user = User::new(name, email);
        user.validate()?;
        self.repository.save(&user).await
    }
}

/// Generic pagination structure
#[derive(Debug, Serialize, Deserialize)]
pub struct Page<T> {
    pub items: Vec<T>,
    pub total: usize,
    pub page: usize,
    pub per_page: usize,
}

impl<T> Page<T> {
    pub fn new(items: Vec<T>, total: usize, page: usize, per_page: usize) -> Self {
        Self {
            items,
            total,
            page,
            per_page,
        }
    }

    pub fn has_next(&self) -> bool {
        self.page * self.per_page < self.total
    }
}

/// Trait with associated types
pub trait Cacheable {
    type Key;
    type Value;

    fn get(&self, key: &Self::Key) -> Option<&Self::Value>;
    fn set(&mut self, key: Self::Key, value: Self::Value);
}

/// Macro for generating CRUD operations
macro_rules! crud_impl {
    ($entity:ident) => {
        paste::paste! {
            pub async fn [<create_ $entity:lower>](&self, item: $entity) -> Result<u64, UserError> {
                self.repository.save(&item).await
            }

            pub async fn [<delete_ $entity:lower>](&self, id: u64) -> Result<(), UserError> {
                self.repository.delete(id).await
            }
        }
    };
}

// Use the macro
crud_impl!(User);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_user_validation() {
        let user = User::new("John".to_string(), "john@example.com".to_string());
        assert!(user.validate().is_ok());

        let invalid_user = User::new("".to_string(), "invalid".to_string());
        assert!(invalid_user.validate().is_err());
    }

    #[tokio::test]
    async fn test_service_creation() {
        // Test would go here with mock repository
    }
}