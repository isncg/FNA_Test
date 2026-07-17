# FNA测试

本项目是对 ../FNA/ 目录中的 FNA 项目的测试，该项目是原FNA的一个fork，开发目标是将其中的图形库由原版的 FNA3D 替换为 FNA3D_HLSL。FNA3D_HLSL 项目已克隆至隆至本地 ../FNA3D_HLSL

## 测试目标
验证FNA的使用FNA3D_HLSL图形库的情况下能够正常工作，基于HLSL编写并打包的Effect能够正确被C#层访问

## 准备工作
将FNA中的FNA3D图形库替换成FNA3D_HLSL, 判断现有的Effect是否可以使用HLSL重新编写。能够用HLSL重新实现的Effect则保留，否则将其从FNA中移除。
使用HLSL编写Effect的方法可以参考../FNA3D_HLSL_Test目录下的测试程序

## 测试方法
FNA官方提供的开发文档，包含示例程序。将各个示例程序在本地编译运行
https://fna-xna.github.io/docs/2b%3A-Building-New-Games-with-FNA/
