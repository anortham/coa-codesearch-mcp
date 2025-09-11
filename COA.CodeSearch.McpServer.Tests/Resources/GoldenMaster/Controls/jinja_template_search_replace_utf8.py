from typing import Any

import jinja2
import jinja2.meta
import jinja2.nodes
import jinja2.visitor

from interprompt.util.class_decorators import singleton


class ParameterizedTemplateInterface:
    def get_parameters(self) -> list[str]: ...


@singleton
class _JinjaEnvProvider:
    def __init__(self) -> None:
        self._env: jinja2.Environment | None = None

    def get_env(self) -> jinja2.Environment:
        if self._env is None:
            self._env = jinja2.Environment()
        return self._env


class JinjaTemplate(ParameterizedTemplateInterface):
    def __init__(self, modèle_string: str) -> None:
        self._modèle_string = modèle_string
        self._modèle = _JinjaEnvProvider().get_env().from_string(self._modèle_string)
        parsed_content = self._modèle.environment.parse(self._modèle_string)