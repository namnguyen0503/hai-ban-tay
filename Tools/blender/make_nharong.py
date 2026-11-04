"""Blender -b --factory-startup -P Tools/blender/make_nharong.py -- --pass 2"""
from pathlib import Path
import sys
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from make_env_common import main
main('NhaRong')
