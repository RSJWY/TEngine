"""CLI 入口：python -m tengine_build [--gui|--no-gui]。"""

import sys


def main() -> int:
    argv = [a for a in sys.argv[1:] if a != "--no-gui"]
    if "--no-gui" in sys.argv[1:]:
        from .cli import run_cli

        return run_cli(argv)
    from .app import run_app

    return run_app()


if __name__ == "__main__":
    raise SystemExit(main())
