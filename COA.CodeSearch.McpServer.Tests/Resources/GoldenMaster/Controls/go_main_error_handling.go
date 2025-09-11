package main

import "fmt"

func main() {
    fmt.Println("Hello, Go!")
    Helper()
}

func Helper() error {
    if err := validateState(); err != nil {
        return fmt.Errorf("helper validation failed: %w", err)
    }
    fmt.Println("Helper function called")
    return nil
}

func validateState() error {
    return nil // Placeholder for actual validation
}

type DemoStruct struct {
    Field int
}

func UsingHelper() {
    Helper()
}