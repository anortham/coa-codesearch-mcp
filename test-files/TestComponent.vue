<template>
  <div class="test-component">
    <h1>{{ title }}</h1>
    <button @click="handleClick">{{ buttonText }}</button>
    <UserList :users="users" @user-selected="onUserSelected" />
  </div>
</template>

<script lang="ts">
import { defineComponent, ref, computed } from 'vue'
import UserList from './UserList.vue'

interface User {
  id: number;
  name: string;
  email: string;
}

export default defineComponent({
  name: 'TestComponent',
  components: {
    UserList
  },
  props: {
    initialTitle: {
      type: String,
      default: 'Default Title'
    }
  },
  setup(props) {
    const title = ref(props.initialTitle)
    const users = ref<User[]>([])
    
    const buttonText = computed(() => {
      return users.value.length > 0 ? 'Clear Users' : 'Load Users'
    })
    
    const handleClick = () => {
      if (users.value.length > 0) {
        users.value = []
      } else {
        loadUsers()
      }
    }
    
    const loadUsers = async () => {
      try {
        const response = await fetch('/api/users')
        users.value = await response.json()
      } catch (error) {
        console.error('Failed to load users:', error)
      }
    }
    
    const onUserSelected = (user: User) => {
      console.log('Selected user:', user)
    }
    
    return {
      title,
      users,
      buttonText,
      handleClick,
      onUserSelected
    }
  }
})
</script>

<style scoped>
.test-component {
  padding: 20px;
}

button {
  background: blue;
  color: white;
  padding: 10px 20px;
  border: none;
  border-radius: 4px;
  cursor: pointer;
}

button:hover {
  background: darkblue;
}
</style>