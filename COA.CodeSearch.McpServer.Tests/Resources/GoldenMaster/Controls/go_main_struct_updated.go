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
    Field string `json:"field"`
    ID    int64  `json:"id"`
}

func UsingHelper() {
    Helper()
}