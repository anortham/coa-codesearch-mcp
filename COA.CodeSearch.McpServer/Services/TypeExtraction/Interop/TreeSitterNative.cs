using System;
using System.Runtime.InteropServices;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction.Interop;

internal static class TreeSitterNative
{
    public enum TSInputEncoding : uint
    {
        UTF8 = 0,
        UTF16 = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TSPoint
    {
        public uint row;
        public uint column;
    }

    // TSNode layout per Tree-sitter C API
    [StructLayout(LayoutKind.Sequential)]
    public struct TSNode
    {
        private IntPtr context0;
        private IntPtr context1;
        private IntPtr context2;
        private IntPtr context3;
        private IntPtr id;
        private IntPtr tree;
    }

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ts_parser_new();

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ts_parser_delete(IntPtr parser);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ts_parser_set_language(IntPtr parser, IntPtr language);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ts_parser_parse_string_encoding(
        IntPtr parser,
        IntPtr old_tree,
        byte[] input,
        uint length,
        TSInputEncoding encoding);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern void ts_tree_delete(IntPtr tree);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern TSNode ts_tree_root_node(IntPtr tree);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint ts_node_child_count(TSNode node);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern TSNode ts_node_child(TSNode node, uint index);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr ts_node_type(TSNode node);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern TSPoint ts_node_start_point(TSNode node);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern TSPoint ts_node_end_point(TSNode node);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint ts_node_start_byte(TSNode node);

    [DllImport("tree-sitter", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint ts_node_end_byte(TSNode node);
}

