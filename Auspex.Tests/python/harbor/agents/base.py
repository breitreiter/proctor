"""A stand-in for Harbor's BaseAgent: only what proctor's agents use of it."""
from pathlib import Path


class BaseAgent:
    def __init__(self, logs_dir=None, *args, **kwargs):
        self.logs_dir = Path(logs_dir) if logs_dir is not None else None
