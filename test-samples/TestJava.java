package com.example.test;

import java.util.List;
import java.util.ArrayList;
import java.util.Optional;

/**
 * Test class for Java type extraction
 */
@Service
@Transactional
public class UserService implements IUserService, Serializable {
    private static final long serialVersionUID = 1L;

    @Autowired
    private UserRepository userRepository;

    private final Logger logger = LoggerFactory.getLogger(UserService.class);

    public UserService(UserRepository repository) {
        this.userRepository = repository;
    }

    @Override
    @Cacheable("users")
    public Optional<User> findUserById(Long id) throws UserNotFoundException {
        logger.debug("Finding user by id: {}", id);
        return userRepository.findById(id)
            .orElseThrow(() -> new UserNotFoundException("User not found: " + id));
    }

    @Async
    public CompletableFuture<List<User>> findAllUsersAsync() {
        return CompletableFuture.supplyAsync(() -> {
            return userRepository.findAll();
        });
    }

    // Inner class example
    public static class UserStatistics {
        private long totalUsers;
        private long activeUsers;

        public UserStatistics(long total, long active) {
            this.totalUsers = total;
            this.activeUsers = active;
        }

        public double getActivePercentage() {
            return totalUsers > 0 ? (double) activeUsers / totalUsers * 100 : 0;
        }
    }

    // Java 14+ record
    public record UserDto(Long id, String name, String email, boolean active) {
        public UserDto {
            // Compact constructor
            if (name == null || name.isBlank()) {
                throw new IllegalArgumentException("Name cannot be empty");
            }
        }
    }

    // Enum with methods
    public enum UserRole {
        ADMIN("Administrator", 3),
        USER("Standard User", 1),
        MODERATOR("Moderator", 2);

        private final String displayName;
        private final int accessLevel;

        UserRole(String displayName, int accessLevel) {
            this.displayName = displayName;
            this.accessLevel = accessLevel;
        }

        public boolean hasHigherAccessThan(UserRole other) {
            return this.accessLevel > other.accessLevel;
        }
    }
}