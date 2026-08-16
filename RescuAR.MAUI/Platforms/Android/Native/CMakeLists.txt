cmake_minimum_required(VERSION 3.22)

project(native_bridge LANGUAGES C CXX)

add_library(
    native_bridge
    SHARED
    native_bridge.cpp
)

target_compile_features(
    native_bridge
    PRIVATE
    cxx_std_17
)

find_library(
    android-lib
    android
)

find_library(
    log-lib
    log
)

target_link_libraries(
    native_bridge
    PRIVATE
    ${android-lib}
    ${log-lib}
)
