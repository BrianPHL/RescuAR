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

# Keep the bridge ABI intentionally narrow. Only functions explicitly marked
# with visibility("default") in native_bridge.cpp may be exported.
set_target_properties(
    native_bridge
    PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
)

target_compile_options(
    native_bridge
    PRIVATE
    -Wall
    -Wextra
    -Werror
    $<$<CONFIG:Release>:-O2>
    $<$<CONFIG:Release>:-g0>
)

target_link_options(
    native_bridge
    PRIVATE
    -Wl,--build-id=sha1
    -Wl,-z,max-page-size=16384
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

# Release artifacts distributed in APK/AAB packages must not retain compiler
# debug sections. The separately generated provenance manifest retains hashes,
# tool versions, ABI, dependencies, and exported-symbol evidence.
if(CMAKE_BUILD_TYPE STREQUAL "Release" AND CMAKE_STRIP)
    add_custom_command(
        TARGET native_bridge
        POST_BUILD
        COMMAND "${CMAKE_STRIP}" --strip-unneeded "$<TARGET_FILE:native_bridge>"
        VERBATIM
    )
endif()
