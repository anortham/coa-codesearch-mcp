export class DemoClass {
    value: number;
    constructor(value: number) {
        this.value = value;
    }
    printValue() {
        console.log(this.value);
    }
}

export function helperFunction() {
    const demo = new DemoClass(42);
    demo.printValue();
}

helperFunction();
