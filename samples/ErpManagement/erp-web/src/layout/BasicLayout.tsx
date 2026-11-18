import React, { useMemo } from 'react';
import { ProLayout } from '@ant-design/pro-components';
import { Dropdown, Space, Tag } from 'antd';
import { LogoutOutlined, UserOutlined, SwapOutlined } from '@ant-design/icons';
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth, getTenantId } from '../auth/AuthContext';
import { filterMenu, menuTree } from './menu';

export default function BasicLayout() {
  const { userName, logout, hasPermission } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();

  const menuData = useMemo(() => filterMenu(menuTree, hasPermission), [hasPermission]);

  return (
    <ProLayout
      title="CrestCreates ERP"
      logo={<AppstoreLogo />}
      layout="mix"
      fixSiderbar
      fixedHeader
      location={{ pathname: location.pathname }}
      route={{ path: '/', routes: menuData }}
      menuItemRender={(item, dom) => (
        <Link to={item.path ?? '/'}>{dom}</Link>
      )}
      actionsRender={() => [
        <Tag key="tenant" icon={<SwapOutlined />} color="blue">
          租户: {getTenantId()}
        </Tag>,
        <Dropdown
          key="user"
          menu={{
            items: [
              {
                key: 'logout',
                icon: <LogoutOutlined />,
                label: '退出登录',
                onClick: () => {
                  logout();
                  navigate('/login');
                },
              },
            ],
          }}
        >
          <Space style={{ cursor: 'pointer' }}>
            <UserOutlined />
            {userName || '未知用户'}
          </Space>
        </Dropdown>,
      ]}
    >
      <Outlet />
    </ProLayout>
  );
}

function AppstoreLogo() {
  return (
    <div
      style={{
        width: 32,
        height: 32,
        borderRadius: 6,
        background: '#1677ff',
        color: '#fff',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontWeight: 700,
      }}
    >
      E
    </div>
  );
}