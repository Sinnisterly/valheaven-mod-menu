cmake_minimum_required(VERSION 3.16)
project(ValheavenVersionProxy C)

add_library(version SHARED
  dllmain.c
  mono_host.c
  payload.rc
)

set_target_properties(version PROPERTIES
  OUTPUT_NAME "version"
  PREFIX ""
  SUFFIX ".dll"
)

target_link_options(version PRIVATE "/DEF:${CMAKE_CURRENT_SOURCE_DIR}/version.def")
target_compile_definitions(version PRIVATE WIN32_LEAN_AND_MEAN _CRT_SECURE_NO_WARNINGS)
