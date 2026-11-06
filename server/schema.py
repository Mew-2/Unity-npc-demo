from typing import Any

from pydantic import BaseModel, Field


class AnchorInfo(BaseModel):
    id: str
    desc: str = ""


class EntityInfo(BaseModel):
    id: str
    kind: str
    at: str


class WorldSnapshot(BaseModel):
    anchors: list[AnchorInfo] = Field(default_factory=list)
    entities: list[EntityInfo] = Field(default_factory=list)


class DirectRequest(BaseModel):
    player_text: str
    world: WorldSnapshot


class DirectResponse(BaseModel):
    say: str = ""
    commands: list[dict[str, Any]] = Field(default_factory=list)
