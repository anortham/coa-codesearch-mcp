package main

import "fmt"

func main() {
    fmt.Println("Hello, Go!")
    Helper()
}

func Helper() {
    fmt.Println("Helper function called")
}

type DemoStruct struct {
    Field int
}

func UsingHelper() {
    Helper()
}
