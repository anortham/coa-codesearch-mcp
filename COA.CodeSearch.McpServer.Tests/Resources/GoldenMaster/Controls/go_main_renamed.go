package main

import "fmt"

func main() {
    fmt.Println("Hello, Go!")
    HelperHandler()
}

func HelperHandler() {
    fmt.Println("Helper function called")
}

type DemoStruct struct {
    Field int
}

func UsingHelperHandler() {
    HelperHandler()
}