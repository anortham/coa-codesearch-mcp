package userservice

import (
    "context"
    "errors"
    "fmt"
    "sync"
    "time"
)

// UserService handles user-related operations
type UserService struct {
    repo     UserRepository
    cache    Cache
    logger   Logger
    mu       sync.RWMutex
    settings *ServiceSettings
}

// UserRepository defines the interface for user data access
type UserRepository interface {
    FindByID(ctx context.Context, id string) (*User, error)
    FindAll(ctx context.Context) ([]*User, error)
    Save(ctx context.Context, user *User) error
    Delete(ctx context.Context, id string) error
}

// User represents a user entity
type User struct {
    ID        string    `json:"id" db:"user_id"`
    Name      string    `json:"name" db:"user_name"`
    Email     string    `json:"email" db:"email"`
    CreatedAt time.Time `json:"created_at" db:"created_at"`
    Active    bool      `json:"active" db:"is_active"`
}

// ServiceSettings contains configuration
type ServiceSettings struct {
    MaxRetries    int
    Timeout       time.Duration
    EnableCaching bool
}

// NewUserService creates a new UserService instance
func NewUserService(repo UserRepository, cache Cache, logger Logger) *UserService {
    return &UserService{
        repo:   repo,
        cache:  cache,
        logger: logger,
        settings: &ServiceSettings{
            MaxRetries:    3,
            Timeout:       30 * time.Second,
            EnableCaching: true,
        },
    }
}

// GetUser retrieves a user by ID with caching
func (s *UserService) GetUser(ctx context.Context, id string) (*User, error) {
    // Check cache first
    if s.settings.EnableCaching {
        if cached, ok := s.cache.Get(id); ok {
            return cached.(*User), nil
        }
    }

    user, err := s.repo.FindByID(ctx, id)
    if err != nil {
        return nil, fmt.Errorf("failed to get user: %w", err)
    }

    // Cache the result
    if s.settings.EnableCaching && user != nil {
        s.cache.Set(id, user, 5*time.Minute)
    }

    return user, nil
}

// ProcessUsersAsync processes users concurrently
func (s *UserService) ProcessUsersAsync(ctx context.Context, processor func(*User) error) error {
    users, err := s.repo.FindAll(ctx)
    if err != nil {
        return err
    }

    var wg sync.WaitGroup
    errChan := make(chan error, len(users))

    for _, user := range users {
        wg.Add(1)
        go func(u *User) {
            defer wg.Done()
            if err := processor(u); err != nil {
                errChan <- fmt.Errorf("error processing user %s: %w", u.ID, err)
            }
        }(user)
    }

    wg.Wait()
    close(errChan)

    // Collect errors
    var errs []error
    for err := range errChan {
        errs = append(errs, err)
    }

    if len(errs) > 0 {
        return fmt.Errorf("processing failed with %d errors", len(errs))
    }

    return nil
}

// Generic type constraint example (Go 1.18+)
type Number interface {
    int | int64 | float32 | float64
}

// Sum is a generic function
func Sum[T Number](values []T) T {
    var sum T
    for _, v := range values {
        sum += v
    }
    return sum
}

// Cache interface
type Cache interface {
    Get(key string) (interface{}, bool)
    Set(key string, value interface{}, ttl time.Duration)
    Delete(key string)
}

// Logger interface
type Logger interface {
    Debug(msg string, args ...interface{})
    Info(msg string, args ...interface{})
    Error(msg string, args ...interface{})
}