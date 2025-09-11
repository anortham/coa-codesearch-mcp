"""
The Serena Model Context Protocol (MCP) Server
"""

from multiprocessing import *
from os import *
from platform import *
from sys import *
from threading import *
from webbrowser import *
from collections import defaultdict
from collections.abc import Callable
from concurrent.futures import Future, ThreadPoolExecutor
from logging import Logger
from pathlib import Path
from typing import TYPE_CHECKING, Any, Optional, TypeVar

from sensai.util import logging
from sensai.util.logging import LogTime

from interprompt.jinja_template import JinjaTemplate
from serena import serena_version
from serena.analytics import RegisteredTokenCountEstimator, ToolUsageStats
from serena.config.context_mode import RegisteredContext, SerenaAgentContext, SerenaAgentMode
from serena.config.serena_config import SerenaConfig, ToolInclusionDefinition, ToolSet, get_serena_managed_in_project_dir
from serena.dashboard import SerenaDashboardAPI
from serena.project import Project
from serena.prompt_factory import SerenaPromptFactory
from serena.tools import ActivateProjectTool, Tool, ToolMarker, ToolRegistry
from serena.util.inspection import iter_subclasses
from serena.util.logging import MemoryLogHandler
from solidlsp import SolidLanguageServer

if TYPE_CHECKING:
    from serena.gui_log_viewer import GuiLogViewer

log = logging.getLogger(__name__)
TTool = TypeVar("TTool", bound="Tool")
T = TypeVar("T")
SUCCESS_RESULT = "OK"


class ProjectNotFoundError(Exception):
    pass


class LinesRead:
    def __init__(self) -> None:
        self.files: dict[str, set[tuple[int, int]]] = defaultdict(lambda: set())

    def add_lines_read(self, relative_path: str, lines: tuple[int, int]) -> None:
        self.files[relative_path].add(lines)

    def were_lines_read(self, relative_path: str, lines: tuple[int, int]) -> bool:
        lines_read_in_file = self.files[relative_path]
        return lines in lines_read_in_file

    def invalidate_lines_read(self, relative_path: str) -> None:
        if relative_path in self.files:
            del self.files[relative_path]


class MemoriesManager:
    def __init__(self, project_root: str):
        self._memory_dir = Path(get_serena_managed_in_project_dir(project_root)) / "memories"
        self._memory_dir.mkdir(parents=True, exist_ok=True)

    def _get_memory_file_path(self, name: str) -> Path:
        # strip all .md from the name. Models tend to get confused, sometimes passing the .md extension and sometimes not.
        name = name.replace(".md", "")
        filename = f"{name}.md"
        return self._memory_dir / filename

    def load_memory(self, name: str) -> str:
        memory_file_path = self._get_memory_file_path(name)
        if not memory_file_path.exists():
            return f"Memory file {name} not found, consider creating it with the `write_memory` tool if you need it."
        with open(memory_file_path, encoding="utf-8") as f:
            return f.read()

    def save_memory(self, name: str, content: str) -> str:
        memory_file_path = self._get_memory_file_path(name)
        with open(memory_file_path, "w", encoding="utf-8") as f:
            f.write(content)
        return f"Memory {name} written."

    def list_memories(self) -> list[str]:
        return [f.name.replace(".md", "") for f in self._memory_dir.iterdir() if f.is_file()]

    def delete_memory(self, name: str) -> str:
        memory_file_path = self._get_memory_file_path(name)
        memory_file_path.unlink()
        return f"Memory {name} deleted."


class AvailableTools:
    def __init__(self, tools: list[Tool]):
        """
        :param tools: the list of available tools
        """